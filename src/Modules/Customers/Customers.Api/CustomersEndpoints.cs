using Customers.Application;
using Customers.Application.Commands;
using Customers.Application.Queries;
using Customers.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Customers.Api;

/// <summary>
/// Fiches clients du restaurant sélectionné dans l'access token. Aucune route ne prend de tenant : ce n'est jamais une
/// valeur de requête. L'hôte chaîne l'autorisation (<c>Tenancy.TenantMemberPolicy</c>) sur le groupe renvoyé. Les fiches
/// contiennent des données personnelles : la LECTURE exige <see cref="CustomersPermissions.View"/> ; modification et
/// consentements : <see cref="CustomersPermissions.Manage"/> ; effacement : <see cref="CustomersPermissions.Anonymize"/> ;
/// réglages : <see cref="CustomersPermissions.Configure"/>.
/// </summary>
public static class CustomersEndpoints
{
    private const string View = CustomersPermissions.View;
    private const string Manage = CustomersPermissions.Manage;
    private const string GetCustomerRoute = "GetCustomer";

    public static RouteGroupBuilder MapCustomersEndpoints(this IEndpointRouteBuilder routes)
    {
        var customers = routes.MapGroup("/customers").WithTags("Customers");

        // Avant « /{id} » : routes fixes du module.
        customers.MapGet("/settings/contact-policy", async (
            IQueryHandler<GetContactPolicyQuery, ContactPolicyDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetContactPolicyQuery(), ct)))
            .RequirePermission(View);

        customers.MapPut("/settings/contact-policy", (
            ContactPolicyRequest request, ICommandHandler<SetContactPolicyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetContactPolicyCommand(request.Requirement), ct)))
            .RequirePermission(CustomersPermissions.Configure);

        // Import d'un fichier (500 lignes au plus) : chaque ligne est créée, complétée ou refusée seule.
        customers.MapPost("/import", async (
            ImportCustomersRequest request, ICommandHandler<ImportCustomersCommand, CustomerImportResultDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ImportCustomersCommand(request.Rows, request.ConsentCertified, request.ConsentSource), ct)))
            .RequirePermission(Manage);

        // Fusion d'un doublon dans la fiche {id} : l'historique des autres modules suit (événement CustomerMerged).
        customers.MapPost("/{id:guid}/merge", (
            Guid id, MergeRequest request, ICommandHandler<MergeCustomersCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MergeCustomersCommand(id, request.DuplicateId), ct)))
            .RequirePermission(Manage);

        // Droit d'accès : tout ce que le module garde du client (fiche, consentements et leur historique, notes).
        customers.MapGet("/{id:guid}/export", async (
            Guid id, HttpContext http, IQueryHandler<ExportCustomerDataQuery, CustomerExportDto> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new ExportCustomerDataQuery(id), ct));
        }).RequirePermission(Manage);

        customers.MapGet("/{id:guid}/notes", async (
            Guid id, IQueryHandler<ListCustomerNotesQuery, IReadOnlyList<CustomerNoteDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListCustomerNotesQuery(id), ct)))
            .RequirePermission(View);

        // L'auteur est l'utilisateur de l'access token, jamais une valeur de la requête.
        customers.MapPost("/{id:guid}/notes", async (
            Guid id, NoteRequest request, HttpContext http, ICommandHandler<AddCustomerNoteCommand, Guid> handler, CancellationToken ct) =>
        {
            var author = Guid.TryParse(http.User.FindFirst("sub")?.Value, out var user) ? user : (Guid?)null;
            return TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(new AddCustomerNoteCommand(id, request.Text, author), ct)));
        }).RequirePermission(Manage);

        customers.MapDelete("/{id:guid}/notes/{noteId:guid}", (
            Guid id, Guid noteId, ICommandHandler<DeleteCustomerNoteCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DeleteCustomerNoteCommand(id, noteId), ct)))
            .RequirePermission(Manage);

        customers.MapPost("/", async (
            CreateCustomerRequest request, ICommandHandler<CreateCustomerCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateCustomerCommand(request.Name, request.Email, request.Phone, request.Tags, request.Preferences), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetCustomerRoute, new { id });
        }).RequirePermission(Manage);

        customers.MapGet("/", async (
            string? name,
            string? email,
            string? phone,
            string? tag,
            bool? includeAnonymized,
            int? page,
            int? pageSize,
            IQueryHandler<SearchCustomersQuery, IReadOnlyList<CustomerDto>> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(
                new SearchCustomersQuery(name, email, phone, tag, includeAnonymized ?? false, skip, take), ct))))
            .RequirePermission(View);

        customers.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetCustomerByIdQuery, CustomerDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetCustomerByIdQuery(id), ct)))
            .WithName(GetCustomerRoute)
            .RequirePermission(View);

        customers.MapPut("/{id:guid}/name", (
            Guid id, NameRequest request, ICommandHandler<RenameCustomerCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameCustomerCommand(id, request.Name), ct)))
            .RequirePermission(Manage);

        customers.MapPut("/{id:guid}/contact", (
            Guid id, ContactRequest request, ICommandHandler<UpdateCustomerContactCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new UpdateCustomerContactCommand(id, request.Email, request.Phone), ct)))
            .RequirePermission(Manage);

        customers.MapPost("/{id:guid}/contact-policy/apply", (
            Guid id, ICommandHandler<ApplyContactPolicyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ApplyContactPolicyCommand(id), ct)))
            .RequirePermission(Manage);

        customers.MapPost("/{id:guid}/tags", (
            Guid id, TagRequest request, ICommandHandler<AddCustomerTagCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AddCustomerTagCommand(id, request.Tag), ct)))
            .RequirePermission(Manage);

        // Le tag en paramètre de requête : il peut contenir des espaces.
        customers.MapDelete("/{id:guid}/tags", (
            Guid id, string? tag, ICommandHandler<RemoveCustomerTagCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveCustomerTagCommand(id, tag), ct)))
            .RequirePermission(Manage);

        customers.MapPut("/{id:guid}/preferences", (
            Guid id, PreferencesInput request, ICommandHandler<UpdateCustomerPreferencesCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new UpdateCustomerPreferencesCommand(id, request), ct)))
            .RequirePermission(Manage);

        customers.MapPost("/{id:guid}/anonymize", (
            Guid id, ICommandHandler<AnonymizeCustomerCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AnonymizeCustomerCommand(id), ct)))
            .RequirePermission(CustomersPermissions.Anonymize);

        MapConsents(customers.MapGroup("/{id:guid}/consents"));
        return customers;
    }

    private static void MapConsents(RouteGroupBuilder consents)
    {
        consents.MapGet("/", async (
            Guid id, IQueryHandler<ListCustomerConsentsQuery, IReadOnlyList<ConsentDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListCustomerConsentsQuery(id), ct)))
            .RequirePermission(View);

        consents.MapGet("/{channel}/marketing-permission", async (
            Guid id, CommunicationChannel channel, IQueryHandler<GetMarketingPermissionQuery, MarketingPermissionDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetMarketingPermissionQuery(id, channel), ct)))
            .RequirePermission(View);

        consents.MapPost("/grant", (
            Guid id, GrantConsentRequest request, ICommandHandler<GrantConsentCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new GrantConsentCommand(id, request.Channel, request.Source), ct)))
            .RequirePermission(Manage);

        consents.MapPost("/withdraw", (
            Guid id, WithdrawConsentRequest request, ICommandHandler<WithdrawConsentCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(
                new WithdrawConsentCommand(id, request.Channel, request.Reason ?? WithdrawalReason.CustomerRequest, request.Source), ct)))
            .RequirePermission(Manage);
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
