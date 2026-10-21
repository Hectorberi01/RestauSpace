using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SaasRestaurant.BuildingBlocks.Api;

public sealed record IdempotencyMetadata(bool Required);

public static class Idempotency
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotency-Replayed";
    public const int MaxKeyLength = 100;
    public const int MaxBufferedBodyBytes = 1024 * 1024;
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public static readonly DomainError KeyRequired =
        new("idempotency.key_required", "This operation requires an Idempotency-Key header.");

    public static readonly DomainError KeyInvalid =
        new("idempotency.key_invalid", "The Idempotency-Key header must be 1 to 100 visible ASCII characters.");

    public static readonly DomainError KeyReused =
        new("idempotency.key_reused", "This Idempotency-Key was already used for a different request.");

    public static readonly DomainError RequestInProgress =
        new("idempotency.request_in_progress", "A request with this Idempotency-Key is still being processed.");

    public static TBuilder RequireIdempotencyKey<TBuilder>(this TBuilder endpoint)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoint.WithMetadata(new IdempotencyMetadata(Required: true));
        return endpoint;
    }

    public static IServiceCollection AddInMemoryIdempotencyStore(this IServiceCollection services)
    {
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        return services;
    }

    public static IApplicationBuilder UseIdempotencyKeys(this IApplicationBuilder app) => app.UseMiddleware<IdempotencyMiddleware>();

    public static bool IsValidKey(string key) =>
        key.Length is > 0 and <= MaxKeyLength && key.All(character => character is > ' ' and <= '~');
}

public sealed class IdempotencyMiddleware(RequestDelegate next, IClock clock)
{
    public async Task InvokeAsync(HttpContext context, IIdempotencyStore store)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        if (!HttpMethods.IsPost(context.Request.Method) && !HttpMethods.IsPatch(context.Request.Method))
        {
            await next(context);
            return;
        }

        var required = context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>()?.Required ?? false;
        var key = context.Request.Headers[Idempotency.HeaderName].ToString();
        if (string.IsNullOrEmpty(key))
        {
            DomainException.ThrowIf(required, Idempotency.KeyRequired);
            await next(context);
            return;
        }

        DomainException.ThrowIf(!Idempotency.IsValidKey(key), Idempotency.KeyInvalid);
        var scope = ScopeOf(context);
        var hash = await HashAsync(context.Request, context.RequestAborted);
        var existing = await store.TryBeginAsync(scope, key, hash, clock.UtcNow + Idempotency.Retention, context.RequestAborted);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
            {
                IdempotencyTelemetry.Count("key_reused");
                throw new DomainException(Idempotency.KeyReused);
            }

            if (existing.Response is not { } stored)
            {
                IdempotencyTelemetry.Count("in_progress");
                throw new ConcurrencyException(Idempotency.RequestInProgress);
            }

            IdempotencyTelemetry.Count("replayed");
            context.Response.StatusCode = stored.StatusCode;
            if (stored.ContentType is not null)
            {
                context.Response.ContentType = stored.ContentType;
            }

            context.Response.Headers[Idempotency.ReplayedHeaderName] = "true";
            await context.Response.Body.WriteAsync(stored.Body, context.RequestAborted);
            return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        catch
        {
            context.Response.Body = original;
            await store.AbandonAsync(scope, key, CancellationToken.None);
            IdempotencyTelemetry.Count("abandoned");
            throw;
        }

        context.Response.Body = original;
        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            await store.AbandonAsync(scope, key, CancellationToken.None);
            IdempotencyTelemetry.Count("abandoned");
        }
        else
        {
            IdempotencyTelemetry.Count("stored");
            await store.CompleteAsync(
                scope, key, new IdempotentResponse(context.Response.StatusCode, context.Response.ContentType, buffer.ToArray()), CancellationToken.None);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(original, context.RequestAborted);
    }

    private static string ScopeOf(HttpContext context)
    {
        var user = context.User;
        var tenant = user.FindFirst(TenantClaims.TenantId)?.Value ?? "-";
        // Sans compte ni clé, chaque adresse client a son propre périmètre : deux inconnus ne partagent jamais une réponse.
        var principal = user.FindFirst("sub")?.Value ?? user.FindFirst(ApiKeyClaims.CredentialId)?.Value
            ?? "anon:" + (context.Connection.RemoteIpAddress?.ToString() ?? "-");
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value ?? string.Empty;
        var scope = $"{tenant}|{principal}|{context.Request.Method} {route}";
        return scope.Length <= 400 ? scope : scope[..400];
    }

    private static async Task<string> HashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering(Idempotency.MaxBufferedBodyBytes);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Encoding.UTF8.GetBytes(request.Method + " " + request.Path + request.QueryString + "\n"));
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            sha.AppendData(chunk, 0, read);
        }

        request.Body.Position = 0;
        return Convert.ToHexString(sha.GetHashAndReset());
    }
}

public sealed class InMemoryIdempotencyStore(IClock clock) : IIdempotencyStore
{
    private readonly ConcurrentDictionary<(string Scope, string Key), (string Hash, IdempotentResponse? Response, DateTimeOffset ExpiresAtUtc)> _entries = new();

    public Task<IdempotencyEntry?> TryBeginAsync(string scope, string key, string requestHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        while (true)
        {
            if (_entries.TryAdd((scope, key), (requestHash, null, expiresAtUtc)))
            {
                return Task.FromResult<IdempotencyEntry?>(null);
            }

            if (_entries.TryGetValue((scope, key), out var existing))
            {
                if (existing.ExpiresAtUtc > now)
                {
                    return Task.FromResult<IdempotencyEntry?>(new IdempotencyEntry(existing.Hash, existing.Response));
                }

                _entries.TryRemove(new KeyValuePair<(string, string), (string, IdempotentResponse?, DateTimeOffset)>((scope, key), existing));
            }
        }
    }

    public Task CompleteAsync(string scope, string key, IdempotentResponse response, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue((scope, key), out var existing))
        {
            _entries[(scope, key)] = (existing.Hash, response, existing.ExpiresAtUtc);
        }

        return Task.CompletedTask;
    }

    public Task AbandonAsync(string scope, string key, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue((scope, key), out var existing) && existing.Response is null)
        {
            _entries.TryRemove(new KeyValuePair<(string, string), (string, IdempotentResponse?, DateTimeOffset)>((scope, key), existing));
        }

        return Task.CompletedTask;
    }

    public Task<int> PurgeExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var removed = 0;
        foreach (var entry in _entries.Where(entry => entry.Value.ExpiresAtUtc <= nowUtc).ToList())
        {
            if (_entries.TryRemove(entry))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }
}

internal static class IdempotencyTelemetry
{
    public const string Name = "SaasRestaurant.Api";

    private static readonly System.Diagnostics.Metrics.Meter Meter = new(Name);

    private static readonly System.Diagnostics.Metrics.Counter<long> Requests =
        Meter.CreateCounter<long>("saasrestaurant.idempotency.requests", "{request}", "Requests carrying an Idempotency-Key, by outcome.");

    public static void Count(string outcome) => Requests.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
