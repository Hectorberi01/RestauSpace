using Payments.Application.Commands;
using Payments.Domain;

namespace Payments.Application;

public sealed class PlatformCollectionTests
{
    private readonly PaymentsFixture _fixture = new();

    [Fact]
    public async Task The_platform_collects_with_its_own_credentials_once_they_are_stored()
    {
        var collector = new PlatformCollector(_fixture.ProviderStore, _fixture, _fixture);
        var reference = Guid.NewGuid();
        var command = new StartPlatformCollectionCommand("fakepay", reference, _fixture.TenantA.Value, 24900m, "XOF", "Abonnement Pro", "+229 97 00 00 01", null);

        var missing = await Assert.ThrowsAsync<DomainException>(() => new StartPlatformCollectionHandler(collector).HandleAsync(command));
        var onSite = await Assert.ThrowsAsync<DomainException>(() => new StartPlatformCollectionHandler(collector).HandleAsync(command with { Provider = "cash" }));
        _fixture.Secrets[PaymentCredentialReference.ForPlatform(PaymentsFixture.FakePay).Value] = new Dictionary<string, string> { ["api_key"] = "sk_platform" };
        var started = await new StartPlatformCollectionHandler(collector).HandleAsync(command);
        var status = await new GetPlatformCollectionStatusHandler(collector).HandleAsync(new GetPlatformCollectionStatusQuery("fakepay", started.ProviderReference));

        Assert.Equal(PaymentsApplicationErrors.PlatformCollectionUnavailable, missing.Error);
        Assert.Equal(PaymentsApplicationErrors.PlatformCollectionUnavailable, onSite.Error);
        Assert.Equal(("fakepay", _fixture.Gateway.LastReference), (started.Provider, started.ProviderReference));
        var request = Assert.Single(_fixture.Gateway.Started);
        Assert.Equal((reference, "Abonnement Pro", "22997000001", 24900m), (request.PaymentId.Value, request.Label, request.PayerPhone, request.Amount.Amount));
        Assert.Equal("sk_platform", _fixture.GatewayCredentials[^1]["api_key"]);
        Assert.Equal(PlatformCollectionState.Unknown, status.State);
        Assert.Equal("StartPlatformCollectionCommand (fakepay, " + reference + ", 24900 XOF, ***)", command.ToString());
    }
}
