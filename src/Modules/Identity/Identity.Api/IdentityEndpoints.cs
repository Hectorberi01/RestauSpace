using System.Security.Claims;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Commands;
using Identity.Application.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Identity.Api;

/// <summary>
/// Les trois groupes de routes du module, pour que le host enchaîne ses propres règles sur chacun. <c>Auth</c> est
/// anonyme, <c>Me</c> exige un utilisateur connecté et <c>Users</c> la policy back-office fournie par le host.
/// </summary>
public sealed record IdentityRouteGroups(RouteGroupBuilder Auth, RouteGroupBuilder Me, RouteGroupBuilder Users);

public static class IdentityEndpoints
{
    /// <param name="backOfficePolicy">
    /// Policy d'autorisation des routes <c>/users</c>, qui agissent sur N'IMPORTE QUEL compte. Obligatoire : l'oublier
    /// permettrait à tout utilisateur connecté de désactiver n'importe qui.
    /// </param>
    public static IdentityRouteGroups MapIdentityEndpoints(this IEndpointRouteBuilder routes, string backOfficePolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backOfficePolicy);
        return new(MapAuth(routes), MapMe(routes), MapUsers(routes, backOfficePolicy));
    }

    private static RouteGroupBuilder MapAuth(IEndpointRouteBuilder routes)
    {
        var auth = routes.MapGroup("/auth").WithTags("Auth").AllowAnonymous()
            .RequireRateLimiting(IdentityRateLimiting.AuthPolicy)
            .RetryOnConflict();

        auth.MapPost("/register", async (
            RegisterRequest request, ICommandHandler<RegisterUserCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new RegisterUserCommand(request.Email, request.Password), ct);
            // Pas de Location : la seule route qui affiche un compte par id appartient au back-office.
            return TypedResults.Created((string?)null, new UserCreatedResponse(id));
        });

        auth.MapPost("/verify-email", async (
            VerifyEmailRequest request, ICommandHandler<VerifyEmailCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new VerifyEmailCommand(request.Token, request.Password), ct);
            return TypedResults.NoContent();
        });

        // 202 quoi qu'il arrive : la réponse ne doit pas révéler si le compte existe.
        auth.MapPost("/resend-verification", async (
            EmailRequest request, ICommandHandler<ResendEmailVerificationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ResendEmailVerificationCommand(request.Email), ct);
            return TypedResults.Accepted((string?)null);
        });

        // Sans validation en deux étapes : les tokens. Avec : un jeton de seconde étape, à présenter avec le code.
        auth.MapPost("/login", async Task<Results<Ok<AuthTokensDto>, Ok<TwoFactorChallengeResponse>>> (
            LoginRequest request, HttpRequest http, ICommandHandler<LoginCommand, LoginResultDto> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new LoginCommand(request.Email, request.Password, DeviceLabels.From(http.Headers.UserAgent.ToString())), ct);
            return result.Tokens is { } tokens
                ? TypedResults.Ok(tokens)
                : TypedResults.Ok(new TwoFactorChallengeResponse(true, result.TwoFactorChallenge!));
        });

        auth.MapPost("/login/two-factor", async (
            TwoFactorLoginRequest request, HttpRequest http, ICommandHandler<CompleteTwoFactorLoginCommand, AuthTokensDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new CompleteTwoFactorLoginCommand(request.Challenge, request.Code, DeviceLabels.From(http.Headers.UserAgent.ToString())), ct)));

        auth.MapPost("/refresh", async (
            RefreshRequest request,
            HttpContext httpContext,
            ICommandHandler<RefreshSessionCommand, AuthTokensDto> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new RefreshSessionCommand(request.RefreshToken, AfterConflict: httpContext.IsRetry(), TenantId: request.TenantId),
                ct)));

        auth.MapPost("/logout", async (
            RefreshTokenRequest request, ICommandHandler<LogoutCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new LogoutCommand(request.RefreshToken), ct);
            return TypedResults.NoContent();
        });

        auth.MapPost("/forgot-password", async (
            EmailRequest request, ICommandHandler<RequestPasswordResetCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RequestPasswordResetCommand(request.Email), ct);
            return TypedResults.Accepted((string?)null);
        });

        auth.MapPost("/reset-password", async (
            ResetPasswordRequest request, ICommandHandler<ResetPasswordCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ResetPasswordCommand(request.Token, request.NewPassword), ct);
            return TypedResults.NoContent();
        });

        auth.MapPost("/accept-invitation", async (
            AcceptInvitationRequest request, ICommandHandler<AcceptInvitationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new AcceptInvitationCommand(request.Token, request.Password), ct);
            return TypedResults.NoContent();
        });

        return auth;
    }

    // L'utilisateur est toujours celui de l'access token, jamais un id venant de la requête. Session de caisse : lecture
    // du profil seulement, et fin de sa propre session.
    private static RouteGroupBuilder MapMe(IEndpointRouteBuilder routes)
    {
        var me = routes.MapGroup("/me").WithTags("Me").RequireAuthorization().RestrictTillSessions().RetryOnConflict();

        me.MapGet("/", async (
            ClaimsPrincipal caller, IQueryHandler<GetUserByIdQuery, UserDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetUserByIdQuery(caller.GetUserId()), ct)));

        // Le seul moyen pour un tenant d'entrer dans un access token : l'appartenance est vérifiée, puis toute route
        // agissant pour un restaurant lit le tenant depuis le token (claim "tid").
        me.MapPost("/tenant", async (
            SelectTenantRequest request,
            ClaimsPrincipal caller,
            ICommandHandler<SelectTenantCommand, TenantAccessTokenDto> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new SelectTenantCommand(caller.GetUserId(), caller.GetSessionId(), request.TenantId ?? Guid.Empty), ct)));

        // Validation en deux étapes : préparer (mot de passe), confirmer (premier code, codes de secours remis une fois),
        // retirer (mot de passe et code).
        me.MapPost("/two-factor/setup", async (
            TwoFactorPasswordRequest request, ClaimsPrincipal caller, ICommandHandler<StartTwoFactorSetupCommand, TwoFactorSetupDto> handler, CancellationToken ct) =>
        {
            var setup = await handler.HandleAsync(new StartTwoFactorSetupCommand(caller.GetUserId(), request.Password), ct);
            return TypedResults.Ok(setup);
        });

        me.MapPost("/two-factor/confirm", async (
            TwoFactorCodeRequest request, ClaimsPrincipal caller, ICommandHandler<ConfirmTwoFactorCommand, TwoFactorRecoveryCodesDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ConfirmTwoFactorCommand(caller.GetUserId(), request.Code), ct)));

        me.MapPost("/two-factor/disable", async (
            DisableTwoFactorRequest request, ClaimsPrincipal caller, ICommandHandler<DisableTwoFactorCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new DisableTwoFactorCommand(caller.GetUserId(), request.Password, request.Code, caller.GetSessionId()), ct);
            return TypedResults.NoContent();
        });

        me.MapPut("/password", async (
            ChangeMyPasswordRequest request,
            ClaimsPrincipal caller,
            ICommandHandler<ChangePasswordCommand> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new ChangePasswordCommand(
                    caller.GetUserId(), request.CurrentPassword, request.NewPassword, caller.GetSessionId()),
                ct);
            return TypedResults.NoContent();
        });

        me.MapPut("/email", async (
            ChangeMyEmailRequest request,
            ClaimsPrincipal caller,
            ICommandHandler<ChangeEmailCommand> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new ChangeEmailCommand(
                    caller.GetUserId(), request.NewEmail, request.CurrentPassword, RequireCurrentPassword: true),
                ct);
            return TypedResults.NoContent();
        });

        // POST avec un body plutôt que DELETE : le mot de passe y transite.
        me.MapPost("/erase", async (
            EraseMyAccountRequest request,
            ClaimsPrincipal caller,
            ICommandHandler<EraseUserCommand> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new EraseUserCommand(caller.GetUserId(), request.CurrentPassword, RequireCurrentPassword: true), ct);
            return TypedResults.NoContent();
        });

        me.MapPut("/profile", async (
            UpdateMyProfileRequest request, ClaimsPrincipal caller, ICommandHandler<UpdateMyProfileCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new UpdateMyProfileCommand(caller.GetUserId(), request.FirstName, request.LastName, request.Phone, request.City), ct);
            return TypedResults.NoContent();
        });

        me.MapPut("/preferences", async (
            MyPreferencesRequest request, ClaimsPrincipal caller, ICommandHandler<ChangeMyPreferencesCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new ChangeMyPreferencesCommand(caller.GetUserId(), request.Language, request.TimeZone, request.ProductNewsEmails), ct);
            return TypedResults.NoContent();
        });

        // Corps brut de l'image ; son type est reconnu aux octets par l'Application, l'en-tête Content-Type est ignoré.
        me.MapPut("/avatar", async (
            HttpRequest http, ClaimsPrincipal caller, ICommandHandler<SetMyAvatarCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetMyAvatarCommand(caller.GetUserId(), await ReadBodyAsync(http, AvatarImage.MaxBytes, ct)), ct);
            return TypedResults.NoContent();
        });

        me.MapDelete("/avatar", async (
            ClaimsPrincipal caller, ICommandHandler<RemoveMyAvatarCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RemoveMyAvatarCommand(caller.GetUserId()), ct);
            return TypedResults.NoContent();
        });

        me.MapGet("/avatar", async (
            ClaimsPrincipal caller, HttpResponse response, IQueryHandler<GetMyAvatarQuery, AvatarImage> handler, CancellationToken ct) =>
        {
            var image = await handler.HandleAsync(new GetMyAvatarQuery(caller.GetUserId()), ct);
            response.Headers.CacheControl = "private, no-cache";
            return TypedResults.File(image.Content, image.ContentType);
        });

        me.MapGet("/sessions", async (
            ClaimsPrincipal caller,
            IQueryHandler<GetUserSessionsQuery, IReadOnlyList<SessionDto>> handler,
            CancellationToken ct) =>
        {
            var current = caller.GetSessionId();
            var sessions = await handler.HandleAsync(new GetUserSessionsQuery(caller.GetUserId()), ct);
            return TypedResults.Ok(sessions.Select(session =>
                new MySessionResponse(session.Id, session.CreatedAtUtc, session.ExpiresAtUtc, session.Id == current, session.DeviceLabel)));
        }).DenyTillSession();

        // Une session de caisse ne peut fermer que la sienne (verrouillage de la caisse).
        me.MapDelete("/sessions/{sessionId:guid}", async (
            Guid sessionId, ClaimsPrincipal caller, ICommandHandler<RevokeSessionCommand> handler, CancellationToken ct) =>
        {
            if (TillSessions.IsTillSession(caller) && caller.GetSessionId() != sessionId)
            {
                throw new ForbiddenException(TillSessions.Forbidden);
            }

            await handler.HandleAsync(new RevokeSessionCommand(caller.GetUserId(), sessionId), ct);
            return TypedResults.NoContent();
        }).AllowTillSession();

        me.MapDelete("/sessions", async (
            ClaimsPrincipal caller, ICommandHandler<RevokeAllSessionsCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RevokeAllSessionsCommand(caller.GetUserId()), ct);
            return TypedResults.NoContent();
        });

        return me;
    }

    /// <summary>Le corps de la requête, lu jusqu'à <paramref name="maxBytes"/> + 1 octet : au-delà, l'Application le refuse.</summary>
    private static async Task<byte[]> ReadBodyAsync(HttpRequest http, int maxBytes, CancellationToken ct)
    {
        // Mis en mémoire tampon : une requête rejouée après un conflit de concurrence relit le même corps.
        http.EnableBuffering(maxBytes + 1);
        http.Body.Position = 0;
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while (buffer.Length <= maxBytes && (read = await http.Body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static RouteGroupBuilder MapUsers(IEndpointRouteBuilder routes, string backOfficePolicy)
    {
        var users = routes.MapGroup("/admin/users").WithTags("Users").RequireAuthorization(backOfficePolicy).RetryOnConflict();

        users.MapPost("/invitations", async (
            EmailRequest request, ICommandHandler<InviteUserCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new InviteUserCommand(request.Email), ct);
            return TypedResults.Created($"/users/{id}", new UserCreatedResponse(id));
        });

        // Renvoie l'invitation d'un compte qui n'a pas encore de mot de passe (nouveau lien, l'ancien reste valable
        // jusqu'à son expiration).
        users.MapPost("/{id:guid}/invitation", async (
            Guid id, ICommandHandler<SendUserInvitationCommand, bool> handler, CancellationToken ct) =>
        {
            if (!await handler.HandleAsync(new SendUserInvitationCommand(id), ct))
            {
                throw new DomainException(IdentityApplicationErrors.NotInvitable);
            }

            return TypedResults.Accepted((string?)null);
        });

        users.MapGet("/", async (
            string? search, Identity.Domain.Users.UserStatus? status, int? page, int? pageSize,
            IQueryHandler<ListUsersQuery, IReadOnlyList<UserDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) =>
                handler.HandleAsync(new ListUsersQuery(search, status, skip, take), ct))));

        users.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetUserByIdQuery, UserDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetUserByIdQuery(id), ct)));

        users.MapPut("/{id:guid}/email", async (
            Guid id, ChangeUserEmailRequest request, ICommandHandler<ChangeEmailCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeEmailCommand(id, request.NewEmail), ct);
            return TypedResults.NoContent();
        });

        users.MapPost("/{id:guid}/lock", async (
            Guid id, ICommandHandler<LockUserCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new LockUserCommand(id), ct);
            return TypedResults.NoContent();
        });

        users.MapPost("/{id:guid}/unlock", async (
            Guid id, ICommandHandler<UnlockUserCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UnlockUserCommand(id), ct);
            return TypedResults.NoContent();
        });

        users.MapPost("/{id:guid}/disable", async (
            Guid id, ICommandHandler<DisableUserCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new DisableUserCommand(id), ct);
            return TypedResults.NoContent();
        });

        users.MapDelete("/{id:guid}", async (
            Guid id, ICommandHandler<EraseUserCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new EraseUserCommand(id), ct);
            return TypedResults.NoContent();
        });

        users.MapDelete("/{id:guid}/sessions", async (
            Guid id, ICommandHandler<RevokeAllSessionsCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RevokeAllSessionsCommand(id), ct);
            return TypedResults.NoContent();
        });

        return users;
    }
}
