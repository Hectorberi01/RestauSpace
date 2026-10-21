using Storefront.Application.Abstractions;
using Storefront.Application.Commands;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Application;

public sealed class HumanCheckTests : IHumanCheck
{
    private readonly StorefrontFixture _fixture = new();

    public bool IsAvailable { get; set; } = true;

    public string? SiteKey => IsAvailable ? "0x4AAA-site" : null;

    public List<string?> Verified { get; } = [];

    Task<bool> IHumanCheck.VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
    {
        Verified.Add(token);
        return Task.FromResult(token == "ok");
    }

    private Task<IssuedSalesAppKey> WebsiteAsync(bool human) =>
        new CreateSalesAppHandler(_fixture.Workspace, _fixture.Clock, this).HandleAsync(new CreateSalesAppCommand(
            "Site", SalesAppKind.Web, ["https://chezawa.bj"], null, null, human));

    [Fact]
    public async Task An_app_that_requires_the_check_publishes_the_site_key_and_refuses_a_bad_token()
    {
        var issued = await WebsiteAsync(human: true);
        await _fixture.SignInAsync(issued.Key, "https://chezawa.bj");
        var verify = new VerifyHumanCheckHandler(_fixture.Workspace, _fixture, this);

        var front = await new GetStorefrontHandler(_fixture.Workspace, _fixture, _fixture, _fixture.Clock, this).HandleAsync(new GetStorefrontQuery());
        var refused = await Assert.ThrowsAsync<DomainException>(() => verify.HandleAsync(new VerifyHumanCheckCommand("bad", null)));
        var missing = await Assert.ThrowsAsync<DomainException>(() => verify.HandleAsync(new VerifyHumanCheckCommand(null, null)));
        await verify.HandleAsync(new VerifyHumanCheckCommand("ok", "203.0.113.4"));

        Assert.Equal("0x4AAA-site", front.App.HumanCheckSiteKey);
        Assert.Equal(StorefrontApplicationErrors.HumanCheckFailed, refused.Error);
        Assert.Equal(StorefrontApplicationErrors.HumanCheckFailed, missing.Error);
        Assert.Equal(new string?[] { "bad", null, "ok" }, Verified);
    }

    [Fact]
    public async Task Without_the_option_nothing_is_checked_and_it_cannot_be_required_before_the_platform_sets_it_up()
    {
        var issued = await WebsiteAsync(human: false);
        await _fixture.SignInAsync(issued.Key, "https://chezawa.bj");
        await new VerifyHumanCheckHandler(_fixture.Workspace, _fixture, this).HandleAsync(new VerifyHumanCheckCommand(null, null));

        IsAvailable = false;
        _fixture.CurrentTenantId = _fixture.TenantA;
        var unavailable = await Assert.ThrowsAsync<DomainException>(() => new UpdateSalesAppHandler(_fixture.Workspace, _fixture.Clock, this)
            .HandleAsync(new UpdateSalesAppCommand(issued.Id, "Site", ["https://chezawa.bj"], null, null, RequireHumanCheck: true)));

        Assert.Empty(Verified);
        Assert.Equal(StorefrontApplicationErrors.HumanCheckUnavailable, unavailable.Error);
    }
}
