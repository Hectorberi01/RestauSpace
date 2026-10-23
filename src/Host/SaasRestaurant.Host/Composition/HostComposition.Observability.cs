using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SaasRestaurant.BuildingBlocks.Application;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace SaasRestaurant.Host;

internal static partial class HostComposition
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        var observability = builder.Configuration.GetSection("Observability");
        builder.Services.AddSingleton<BusinessMetrics>();
        builder.Services.AddScoped<IOutboxConsumer, BusinessMetricsFlow>();
        if (observability.GetValue("PlatformMetrics:Enabled", true))
        {
            builder.Services.AddSingleton(new PlatformMetricsSettings(
                TimeSpan.FromSeconds(Math.Max(15, observability.GetValue("PlatformMetrics:IntervalSeconds", 60)))));
            builder.Services.AddHostedService<PlatformMetricsWorker>();
        }

        var endpoint = observability["OtlpEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return builder;
        }

        var sampleRatio = Math.Clamp(observability.GetValue("TraceSampleRatio", 1.0), 0.0, 1.0);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    observability["ServiceName"] ?? "saasrestaurant-api",
                    serviceVersion: typeof(HostComposition).Assembly.GetName().Version?.ToString())
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampleRatio)))
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = false;
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health");
                    // Jetons dans l'URL (imprimante, client invité de la vente en ligne) : jamais dans une trace.
                    options.EnrichWithHttpRequest = (activity, request) =>
                    {
                        if ((Printing.Api.PrintingEndpoints.MaskDevicePath(request.Path)
                             ?? Storefront.Api.StorefrontEndpoints.MaskGuestTokenPath(request.Path)) is { } masked)
                        {
                            activity.SetTag("url.path", masked);
                            if (activity.GetTagItem("url.full") is string full && request.Path.Value is { Length: > 0 } raw)
                            {
                                activity.SetTag("url.full", full.Replace(raw, masked, StringComparison.Ordinal));
                            }

                            activity.DisplayName = request.Method + " " + masked;
                        }
                    };
                    options.EnrichWithHttpResponse = (activity, response) =>
                    {
                        if (response.HttpContext.User.FindFirst(TenantClaims.TenantId)?.Value is { Length: > 0 } tenant)
                        {
                            activity.SetTag("saasrestaurant.tenant_id", tenant);
                        }
                    };
                })
                .AddHttpClientInstrumentation(options => options.RecordException = false)
                .AddSource(OutboxTelemetry.Name, "Npgsql"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(
                    OutboxTelemetry.Name,
                    BusinessMetrics.Name,
                    "SaasRestaurant.Api",
                    "Npgsql",
                    "Microsoft.AspNetCore.Hosting",
                    "Microsoft.AspNetCore.Server.Kestrel",
                    "Microsoft.AspNetCore.RateLimiting",
                    "Microsoft.AspNetCore.Diagnostics",
                    "Microsoft.AspNetCore.Authentication",
                    "Microsoft.AspNetCore.Authorization",
                    "System.Net.Http"))
            .WithLogging(
                _ => { },
                options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                })
            // http/protobuf pour un collecteur derrière un proxy HTTPS ; en-têtes (jeton) : OTEL_EXPORTER_OTLP_HEADERS.
            .UseOtlpExporter(
                string.Equals(observability["OtlpProtocol"], "http/protobuf", StringComparison.OrdinalIgnoreCase)
                    ? OtlpExportProtocol.HttpProtobuf
                    : OtlpExportProtocol.Grpc,
                new Uri(endpoint));
        return builder;
    }
}
