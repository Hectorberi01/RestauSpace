using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Files.Application;

namespace Files.Api;

public sealed class FilesEndpointsTests(FilesApi api) : IClassFixture<FilesApi>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0];

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        Assert.True(
            response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await AssertStatusAsync(response, status);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        await AssertStatusAsync(response, expected);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Declaration(Guid product, long size = 16, string contentType = "image/png") =>
        new { ownerType = "catalog.product", ownerId = product, fileName = "burger.png", contentType, sizeInBytes = size };

    /// <summary>Déclare, envoie (comme le navigateur) et confirme un fichier ; renvoie (id, résultat de la confirmation).</summary>
    private async Task<(Guid Id, JsonElement Result)> UploadAsync(HttpClient client, Guid product, byte[] content)
    {
        var ticket = await JsonAsync(await client.PostAsJsonAsync("/files/uploads", Declaration(product, content.Length)), HttpStatusCode.Created);
        var id = ticket.GetProperty("fileId").GetGuid();
        api.UploadBytes(new Uri(ticket.GetProperty("uploadUrl").GetString()!), "image/png", content);
        return (id, await JsonAsync(await client.PostAsync($"/files/{id}/complete", null)));
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync($"/files/{Guid.NewGuid()}"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync($"/files/{Guid.NewGuid()}"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Uploading_and_deleting_need_their_own_permissions()
    {
        var tenant = api.NewTenant(denied: [FilesPermissions.Upload, FilesPermissions.Manage]);
        var client = api.ActingFor(tenant);

        await AssertStatusAsync(await client.PostAsJsonAsync("/files/uploads", Declaration(api.NewProduct(tenant))), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.DeleteAsync($"/files/{Guid.NewGuid()}"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_file_is_declared_uploaded_directly_confirmed_listed_and_downloaded()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var product = api.NewProduct(tenant);

        var (id, result) = await UploadAsync(client, product, Png);
        var file = await JsonAsync(await client.GetAsync($"/files/{id}"));
        var listed = await JsonAsync(await client.GetAsync($"/files?ownerType=catalog.product&ownerId={product}"));
        var link = await JsonAsync(await client.GetAsync($"/files/{id}/download"));

        Assert.Equal("Available", result.GetProperty("outcome").GetString());
        Assert.Equal(("Available", "burger.png", 16L), (file.GetProperty("status").GetString(), file.GetProperty("fileName").GetString(), file.GetProperty("sizeInBytes").GetInt64()));
        Assert.False(file.TryGetProperty("storageKey", out _));
        Assert.Equal(id, Assert.Single(listed.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.StartsWith("memory://download/", link.GetProperty("url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disguised_file_is_quarantined_and_never_downloadable()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var executable = new byte[16];
        executable[0] = (byte)'M';
        executable[1] = (byte)'Z';

        var (id, result) = await UploadAsync(client, api.NewProduct(tenant), executable);

        Assert.Equal(("Quarantined", "files.media_file.content_type_mismatch"), (result.GetProperty("outcome").GetString(), result.GetProperty("reason").GetString()));
        await AssertProblemAsync(await client.GetAsync($"/files/{id}/download"), HttpStatusCode.UnprocessableEntity, "files.media_file.not_servable");
    }

    [Fact]
    public async Task Invalid_declarations_are_refused_with_a_stable_code()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var product = api.NewProduct(tenant);

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/files/uploads", Declaration(product, contentType: "application/x-msdownload")),
            HttpStatusCode.UnprocessableEntity,
            "files.media_file.content_type_not_allowed");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/files/uploads", Declaration(Guid.NewGuid())), HttpStatusCode.NotFound, "files.owner.not_found");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/files/uploads", new { ownerType = "reporting.export_job", ownerId = Guid.NewGuid(), fileName = "x.csv", contentType = "text/csv", sizeInBytes = 10 }),
            HttpStatusCode.UnprocessableEntity,
            "files.owner.type_not_uploadable");
        await AssertProblemAsync(
            await client.PostAsync($"/files/{(await JsonAsync(await client.PostAsJsonAsync("/files/uploads", Declaration(product)), HttpStatusCode.Created)).GetProperty("fileId").GetGuid()}/complete", null),
            HttpStatusCode.UnprocessableEntity,
            "files.media_file.not_uploaded");
    }

    [Fact]
    public async Task A_restaurant_never_sees_downloads_nor_deletes_the_files_of_another()
    {
        var owner = api.NewTenant();
        var (id, _) = await UploadAsync(api.ActingFor(owner), api.NewProduct(owner), Png);
        var intruder = api.ActingFor(api.NewTenant());

        await AssertProblemAsync(await intruder.GetAsync($"/files/{id}"), HttpStatusCode.NotFound, "files.media_file.not_found");
        await AssertProblemAsync(await intruder.GetAsync($"/files/{id}/download"), HttpStatusCode.NotFound, "files.media_file.not_found");
        await AssertProblemAsync(await intruder.DeleteAsync($"/files/{id}"), HttpStatusCode.NotFound, "files.media_file.not_found");
        Assert.Equal("Available", (await JsonAsync(await api.ActingFor(owner).GetAsync($"/files/{id}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_deleted_file_is_no_longer_served()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var (id, _) = await UploadAsync(client, api.NewProduct(tenant), Png);

        await AssertStatusAsync(await client.DeleteAsync($"/files/{id}"), HttpStatusCode.NoContent);

        Assert.Equal("Deleted", (await JsonAsync(await client.GetAsync($"/files/{id}"))).GetProperty("status").GetString());
        await AssertProblemAsync(await client.GetAsync($"/files/{id}/download"), HttpStatusCode.UnprocessableEntity, "files.media_file.not_servable");
    }
}
