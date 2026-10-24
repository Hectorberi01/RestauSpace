using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Backups.Application.Abstractions;
using Backups.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Backups.Api;

public sealed class BackupsEndpointsTests(BackupsApi api) : IClassFixture<BackupsApi>
{
    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        Assert.True(
            response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        await AssertStatusAsync(response, expected);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode expected, string code) =>
        Assert.Equal(code, (await JsonAsync(response, expected)).GetProperty("code").GetString());

    private async Task<Guid> CompletedBackupAsync()
    {
        var backup = await JsonAsync(await api.Admin().PostAsync("/admin/backups", null), HttpStatusCode.Accepted);
        var id = backup.GetProperty("id").GetGuid();
        await api.CompleteAsync(id);
        return id;
    }

    [Fact]
    public async Task Every_route_is_reserved_to_platform_administrators()
    {
        await api.ResetAsync();
        var id = Guid.NewGuid();
        (HttpMethod Method, string Path)[] routes =
        [
            (HttpMethod.Get, "/admin/backups"), (HttpMethod.Get, $"/admin/backups/{id}"), (HttpMethod.Post, "/admin/backups"),
            (HttpMethod.Delete, $"/admin/backups/{id}"), (HttpMethod.Post, $"/admin/backups/{id}/download-link"),
            (HttpMethod.Post, $"/admin/backups/{id}/restore"), (HttpMethod.Get, "/admin/backups/restores"),
            (HttpMethod.Post, $"/admin/backups/restores/{id}/cancel"), (HttpMethod.Get, "/admin/backups/agent"), (HttpMethod.Get, "/admin/backups/settings"),
        ];

        foreach (var (method, path) in routes)
        {
            await AssertStatusAsync(await api.Anonymous().SendAsync(new HttpRequestMessage(method, path)), HttpStatusCode.Unauthorized);
            await AssertStatusAsync(await api.Member().SendAsync(new HttpRequestMessage(method, path)), HttpStatusCode.Forbidden);
        }

        Assert.Equal(0L, await api.ScalarAsync<long>("SELECT count(*) FROM backups.backups"));
    }

    [Fact]
    public async Task A_backup_is_accepted_pending_and_a_second_one_conflicts()
    {
        await api.ResetAsync();

        var backup = await JsonAsync(await api.Admin().PostAsync("/admin/backups", null), HttpStatusCode.Accepted);

        Assert.Equal(("Pending", "Manual", BackupsApi.Database), (
            backup.GetProperty("status").GetString(), backup.GetProperty("trigger").GetString(), backup.GetProperty("databaseName").GetString()));
        Assert.Equal(api.AdminId, backup.GetProperty("requestedBy").GetGuid());
        Assert.False(backup.TryGetProperty("storageKey", out _));
        await AssertErrorAsync(await api.Admin().PostAsync("/admin/backups", null), HttpStatusCode.Conflict, "backups.backup.already_in_progress");

        var listed = await JsonAsync(await api.Admin().GetAsync("/admin/backups?page=1&pageSize=10"));
        Assert.Equal(backup.GetProperty("id").GetGuid(), Assert.Single(listed.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        await AssertStatusAsync(await api.Admin().GetAsync($"/admin/backups/{backup.GetProperty("id").GetGuid()}"), HttpStatusCode.OK);
        await AssertErrorAsync(await api.Admin().GetAsync($"/admin/backups/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "backups.backup.not_found");
    }

    [Fact]
    public async Task The_unique_index_refuses_a_second_active_backup_written_behind_the_api()
    {
        await api.ResetAsync();
        _ = await JsonAsync(await api.Admin().PostAsync("/admin/backups", null), HttpStatusCode.Accepted);

        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => api.SqlAsync(
            "INSERT INTO backups.backups (id, database_name, trigger, status, created_at_utc) VALUES ($1, 'es_restaurant', 'Scheduled', 'Running', now())",
            Guid.NewGuid()));

        Assert.Equal("ux_backups_active", refused.ConstraintName);
    }

    [Fact]
    public async Task A_concurrent_request_stopped_by_the_unique_index_is_already_in_progress()
    {
        await api.ResetAsync();
        _ = await JsonAsync(await api.Admin().PostAsync("/admin/backups", null), HttpStatusCode.Accepted);

        // La vérification préalable est passée chez l'autre instance : seul l'index voit la sauvegarde active.
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IBackupRepository>()
            .Add(Backup.RequestManual(BackupsApi.Database, UserId.From(api.AdminId), TimeSpan.FromDays(30), api.Clock));
        var refused = await Assert.ThrowsAsync<DomainException>(() => scope.ServiceProvider.GetRequiredService<IBackupsUnitOfWork>().SaveChangesAsync());

        Assert.Equal(BackupErrors.AlreadyInProgress.Code, refused.Code);
        Assert.Equal(1L, await api.ScalarAsync<long>("SELECT count(*) FROM backups.backups"));
    }

    [Fact]
    public async Task The_download_link_needs_a_valid_code_and_is_a_presigned_get_without_the_secret()
    {
        await api.ResetAsync();
        var id = await CompletedBackupAsync();

        await AssertErrorAsync(
            await api.Admin().PostAsJsonAsync($"/admin/backups/{id}/download-link", new { code = "000000" }), HttpStatusCode.UnprocessableEntity,
            "backups.two_factor.invalid");
        var link = await JsonAsync(await api.Admin().PostAsJsonAsync($"/admin/backups/{id}/download-link", new { code = BackupsApi.ValidCode }));

        var url = new Uri(link.GetProperty("url").GetString()!);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal(("https", "s3.gra.io.cloud.ovh.net"), (url.Scheme, url.Host));
        Assert.Equal($"/restauspace-backups/{BackupsApi.Database}/2026/10/{id}.dump.age", url.AbsolutePath);
        Assert.Equal("AWS4-HMAC-SHA256", query["X-Amz-Algorithm"]);
        Assert.Matches($"^{BackupsApi.AccessKeyId}/[0-9]{{8}}/gra/s3/aws4_request$", query["X-Amz-Credential"]);
        Assert.Equal("300", query["X-Amz-Expires"]);
        Assert.Equal("host", query["X-Amz-SignedHeaders"]);
        Assert.Matches("^[0-9a-f]{64}$", query["X-Amz-Signature"]);
        Assert.StartsWith("attachment;", query["response-content-disposition"]);
        Assert.DoesNotContain(BackupsApi.SecretAccessKey, link.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(api.Clock.UtcNow.AddMinutes(5), link.GetProperty("expiresAtUtc").GetDateTimeOffset());

        // Audit : l'événement porte l'id et l'échéance, jamais l'URL.
        var audited = await api.ScalarAsync<string>("SELECT content::text FROM backups.outbox_messages WHERE type = 'BackupDownloadLinkCreated'");
        Assert.Contains(id.ToString(), audited);
        Assert.DoesNotContain("X-Amz", audited);
    }

    [Fact]
    public async Task The_download_link_is_refused_for_a_backup_that_is_not_completed()
    {
        await api.ResetAsync();
        var pending = await JsonAsync(await api.Admin().PostAsync("/admin/backups", null), HttpStatusCode.Accepted);

        await AssertErrorAsync(
            await api.Admin().PostAsJsonAsync($"/admin/backups/{pending.GetProperty("id").GetGuid()}/download-link", new { code = BackupsApi.ValidCode }),
            HttpStatusCode.Conflict, "backups.backup.not_completed");
    }

    [Fact]
    public async Task A_restore_needs_the_database_name_and_a_code_and_can_be_cancelled_while_pending()
    {
        await api.ResetAsync();
        var id = await CompletedBackupAsync();

        await AssertErrorAsync(
            await api.Admin().PostAsJsonAsync($"/admin/backups/{id}/restore", new { code = BackupsApi.ValidCode, confirmDatabaseName = "es_restaurant_x" }),
            HttpStatusCode.UnprocessableEntity, "backups.restore.confirmation_mismatch");
        await AssertErrorAsync(
            await api.Admin().PostAsJsonAsync($"/admin/backups/{id}/restore", new { code = "111111", confirmDatabaseName = BackupsApi.Database }),
            HttpStatusCode.UnprocessableEntity, "backups.two_factor.invalid");
        var restore = await JsonAsync(
            await api.Admin().PostAsJsonAsync($"/admin/backups/{id}/restore", new { code = BackupsApi.ValidCode, confirmDatabaseName = BackupsApi.Database }),
            HttpStatusCode.Accepted);

        Assert.Equal(("Pending", id), (restore.GetProperty("status").GetString(), restore.GetProperty("backupId").GetGuid()));
        Assert.Equal(
            restore.GetProperty("createdAtUtc").GetDateTimeOffset().AddMinutes(5), restore.GetProperty("notBeforeUtc").GetDateTimeOffset());
        await AssertErrorAsync(await api.Admin().PostAsync("/admin/backups", null), HttpStatusCode.Conflict, "backups.restore.in_progress");

        var restoreId = restore.GetProperty("id").GetGuid();
        var cancelled = await JsonAsync(await api.Admin().PostAsync($"/admin/backups/restores/{restoreId}/cancel", null));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        await AssertErrorAsync(
            await api.Admin().PostAsync($"/admin/backups/restores/{restoreId}/cancel", null), HttpStatusCode.Conflict, "backups.restore.not_cancellable");
        var restores = await JsonAsync(await api.Admin().GetAsync("/admin/backups/restores"));
        Assert.Equal(restoreId, Assert.Single(restores.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Deleting_the_last_completed_backup_is_refused_by_keep_min()
    {
        await api.ResetAsync();
        var first = await CompletedBackupAsync();

        await AssertErrorAsync(await api.Admin().DeleteAsync($"/admin/backups/{first}"), HttpStatusCode.Conflict, "backups.backup.keep_min_reached");

        _ = await CompletedBackupAsync();
        var deleting = await JsonAsync(await api.Admin().DeleteAsync($"/admin/backups/{first}"), HttpStatusCode.Accepted);
        Assert.Equal("Deleting", deleting.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_agent_and_the_settings_are_readable()
    {
        await api.ResetAsync();

        var unseen = await JsonAsync(await api.Admin().GetAsync("/admin/backups/agent"));
        Assert.False(unseen.GetProperty("online").GetBoolean());
        Assert.Equal(JsonValueKind.Null, unseen.GetProperty("host").ValueKind);

        await api.SqlAsync(
            "INSERT INTO backups.agent (id, host, version, last_seen_at_utc, restore_enabled) VALUES (1, 'vps-31d9dc8a', '1.0.0', $1, false)", api.Clock.UtcNow);
        var seen = await JsonAsync(await api.Admin().GetAsync("/admin/backups/agent"));
        Assert.Equal(("vps-31d9dc8a", true, false), (
            seen.GetProperty("host").GetString(), seen.GetProperty("online").GetBoolean(), seen.GetProperty("restoreEnabled").GetBoolean()));

        var settings = await JsonAsync(await api.Admin().GetAsync("/admin/backups/settings"));
        Assert.Equal((BackupsApi.Database, 30, 1, "02:30", 5, true), (
            settings.GetProperty("databaseName").GetString(), settings.GetProperty("retentionDays").GetInt32(), settings.GetProperty("keepMin").GetInt32(),
            settings.GetProperty("dailyAtUtc").GetString(), settings.GetProperty("restoreDelayMinutes").GetInt32(),
            settings.GetProperty("storageConfigured").GetBoolean()));
        Assert.False(settings.TryGetProperty("secretAccessKey", out _));
    }
}
