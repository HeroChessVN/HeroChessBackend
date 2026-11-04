using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace HeroChess.Api.Services;

public static class BotObservability
{
    public static IServiceCollection AddBotObservability(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Observability:Enabled")) return services;
        // Standard OTEL_* settings supply Grafana endpoint/auth; never log their values.
        var endpoint = configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Metrics export requires OTEL_EXPORTER_OTLP_METRICS_ENDPOINT with an HTTPS URL including /v1/metrics.");
        services.AddOpenTelemetry().ConfigureResource(r => r.AddService("herochess-api"))
            .WithMetrics(metrics => metrics
                .AddMeter(BotTelemetry.MeterName, "Microsoft.AspNetCore.Hosting", "System.Runtime", "Npgsql")
                .AddView("herochess.*", new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 30, 90],
                    CardinalityLimit = 100
                })
                .AddOtlpExporter((options, reader) =>
                {
                    options.Endpoint = uri;
                    options.Headers = configuration["OTEL_EXPORTER_OTLP_METRICS_HEADERS"] ?? configuration["OTEL_EXPORTER_OTLP_HEADERS"];
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    options.TimeoutMilliseconds = 5000;
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 15000;
                    reader.PeriodicExportingMetricReaderOptions.ExportTimeoutMilliseconds = 5000;
                }));
        return services;
    }
}
