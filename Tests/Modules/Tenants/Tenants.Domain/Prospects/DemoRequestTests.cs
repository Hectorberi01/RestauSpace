namespace Tenants.Domain.Prospects;

public sealed class DemoRequestTests
{
    private readonly FakeClock _clock = new();

    private DemoRequest Submit(string? email = "awa@example.com", string? phone = null, string? message = null) =>
        DemoRequest.Submit(" Awa Dossou ", " Chez Awa ", email, phone, "Bénin", "Pro", message, _clock);

    [Fact]
    public void Submit_trims_and_keeps_the_request_new()
    {
        var request = Submit(phone: "+229 97 00 00 00");

        Assert.Equal("Awa Dossou", request.ContactName);
        Assert.Equal("Chez Awa", request.RestaurantName);
        Assert.Equal("awa@example.com", request.Email!.Value);
        Assert.Equal("+22997000000", request.Phone!.Value);
        Assert.Equal(DemoRequestStatus.New, request.Status);
        Assert.Equal(_clock.UtcNow, request.SubmittedAtUtc);
    }

    [Fact]
    public void The_submitted_event_carries_no_personal_data()
    {
        var request = Submit();

        var submitted = Assert.IsType<DemoRequestSubmitted>(Assert.Single(request.DomainEvents));
        Assert.Equal(request.Id, submitted.DemoRequestId);
        Assert.Equal(new[] { "DemoRequestId", "OccurredAtUtc" }, typeof(DemoRequestSubmitted).GetProperties().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public void An_email_or_a_phone_is_required()
    {
        var error = Assert.Throws<DomainException>(() => Submit(email: null, phone: " "));
        Assert.Equal(DemoRequestErrors.ContactMissing.Code, error.Error.Code);
    }

    [Theory]
    [InlineData("pas-un-email", null, "common.email.invalid")]
    [InlineData(null, "97000000", "common.phone.invalid")]
    public void Contact_details_are_validated(string? email, string? phone, string code)
    {
        var error = Assert.Throws<DomainException>(() => Submit(email, phone));
        Assert.Equal(code, error.Error.Code);
    }

    [Fact]
    public void Names_are_single_lines()
    {
        var error = Assert.Throws<DomainException>(() =>
            DemoRequest.Submit("Awa\nDossou", "Chez Awa", "awa@example.com", null, "Bénin", null, null, _clock));
        Assert.Equal(DemoRequestErrors.ContactNameInvalid.Code, error.Error.Code);
    }

    [Fact]
    public void The_message_keeps_line_breaks_but_is_limited()
    {
        Assert.Equal("Bonjour\nDeux salles", Submit(message: "Bonjour\r\nDeux salles").Message);
        Assert.Throws<DomainException>(() => Submit(message: new string('a', DemoRequest.MessageMaxLength + 1)));
    }

    [Fact]
    public void Status_changes_are_recorded_once()
    {
        var request = Submit();
        request.ClearDomainEvents();

        request.ChangeStatus(DemoRequestStatus.Contacted, _clock);
        request.ChangeStatus(DemoRequestStatus.Contacted, _clock);

        var changed = Assert.IsType<DemoRequestStatusChanged>(Assert.Single(request.DomainEvents));
        Assert.Equal(DemoRequestStatus.New, changed.PreviousStatus);
        Assert.Equal(DemoRequestStatus.Contacted, request.Status);
        Assert.Throws<DomainException>(() => request.ChangeStatus((DemoRequestStatus)42, _clock));
    }
}
