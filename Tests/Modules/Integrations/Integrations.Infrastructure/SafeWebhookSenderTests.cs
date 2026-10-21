using System.Net;
using Integrations.Application.Abstractions;
using Integrations.Infrastructure.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Integrations.Infrastructure;

/// <summary>Anti-SSRF : les adresses refusées, et un envoi vers une adresse interne n'ouvre aucune connexion.</summary>
public sealed class SafeWebhookSenderTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("2002:0a00:0001::1")]
    [InlineData("2001:db8::1")]
    public void A_non_public_address_is_refused(string address)
    {
        Assert.False(PublicAddresses.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void A_public_address_is_accepted(string address)
    {
        Assert.True(PublicAddresses.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://[::1]/hook")]
    public async Task A_request_to_an_internal_address_is_blocked_before_connecting(string url)
    {
        using var sender = new SafeWebhookSender(new IntegrationsOptions(), NullLogger<SafeWebhookSender>.Instance);

        var result = await sender.SendAsync(new WebhookRequest(new Uri(url), "{}", new Dictionary<string, string>()));

        Assert.Equal(WebhookSendResult.Failure(SafeWebhookSender.BlockedAddress), result);
    }
}
