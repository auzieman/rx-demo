// /src/shared/Telemetry/Metrics.cs
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;

namespace Shared.Telemetry
{
    public static class Metrics
    {
        // Primary meter (registered in OtelBootstrap via AddMeter("rx.meter"))
        public static readonly Meter Meter = new("rx.meter", "1.0.0");
        private static readonly object ComponentSync = new();
        private static readonly Dictionary<string, ComponentHealthState> Components = new(StringComparer.OrdinalIgnoreCase);

        private static readonly ObservableGauge<double> ComponentWeight = Meter.CreateObservableGauge(
            "rx.component.weight_percent",
            ObserveComponentWeight,
            unit: "%",
            description: "Executive weighting assigned to this component for overall health rollups.");

        private static readonly ObservableGauge<double> ComponentHealth = Meter.CreateObservableGauge(
            "rx.component.health_percent",
            ObserveComponentHealth,
            unit: "%",
            description: "Rolling component health score. Starts at 100 and subtracts health penalty for recent errors.");

        private static readonly ObservableGauge<long> ComponentStatus = Meter.CreateObservableGauge(
            "rx.component.health_status",
            ObserveComponentStatus,
            description: "Component health band: 2=green, 1=yellow, 0=red.");

        private static readonly ObservableGauge<long> ComponentRecentErrors = Meter.CreateObservableGauge(
            "rx.component.error_events_5m",
            ObserveComponentRecentErrors,
            description: "Error events recorded by this component in the last five minutes.");

        // API metrics
        public static readonly Counter<long>     ApiRequests     = Meter.CreateCounter<long>("rx.api.requests_total");
        public static readonly Histogram<double> ApiDuration     = Meter.CreateHistogram<double>("rx.api.duration_ms");

        // Messaging / processing metrics
        public static readonly Counter<long>     QueueMessages   = Meter.CreateCounter<long>("rx.queue.messages_total");
        public static readonly Histogram<double> ProcessDuration = Meter.CreateHistogram<double>("rx.process.duration_ms");
        public static readonly Counter<long>     PublishedMessages = Meter.CreateCounter<long>("rx.message.publish_total");

        // Database metrics (names align to views configured in OtelBootstrap)
        public static readonly Counter<long>     DbOpTotal       = Meter.CreateCounter<long>("rx.db.op_total");
        public static readonly Histogram<double> DbDuration      = Meter.CreateHistogram<double>("rx.db.duration_ms");

        // Payload size total
        public static readonly Counter<long> DbPayloadBytes = Meter.CreateCounter<long>("rx.db.payload.bytes");

        // Cache and error metrics
        public static readonly Counter<long>     CacheOps       = Meter.CreateCounter<long>("rx.cache.ops_total");
        public static readonly Histogram<double> CacheDuration  = Meter.CreateHistogram<double>("rx.cache.duration_ms");
        public static readonly Counter<long>     ErrorsTotal    = Meter.CreateCounter<long>("rx.errors_total");

        public static void ConfigureComponent(IConfiguration cfg, string component)
        {
            var weight = ReadDouble(cfg["RxHealth:ComponentWeight"], DefaultWeightFor(component));
            var penalty = ReadDouble(cfg["RxHealth:ErrorPenaltyPercent"], 15);
            RegisterComponent(component, weight, penalty);
        }

        public static void RecordApiRequest(string route, string method, string result, double durationMs)
        {
            ApiRequests.Add(1, BuildTags(
                ("route", route),
                ("method", method),
                ("result", result)));

            ApiDuration.Record(durationMs, BuildTags(
                ("route", route),
                ("method", method)));
        }

        public static void RecordQueueMessage(string queue, string direction, string messageType, string result, double durationMs)
        {
            QueueMessages.Add(1, BuildTags(
                ("queue", queue),
                ("direction", direction),
                ("message_type", messageType),
                ("result", result)));

            ProcessDuration.Record(durationMs, BuildTags(
                ("queue", queue),
                ("message_type", messageType)));
        }

        public static void RecordPublished(string queue, string messageType, string result = "success")
        {
            PublishedMessages.Add(1, BuildTags(
                ("queue", queue),
                ("message_type", messageType),
                ("result", result)));
        }

        public static void RecordDbOperation(string operation, string result, double durationMs, long payloadBytes = 0)
        {
            DbOpTotal.Add(1, BuildTags(
                ("operation", operation),
                ("result", result)));

            DbDuration.Record(durationMs, BuildTags(
                ("operation", operation),
                ("result", result)));

            if (payloadBytes > 0)
                DbPayloadBytes.Add(payloadBytes, BuildTags(("operation", operation)));
        }

        public static void RecordCache(string cache, string operation, string result, double durationMs)
        {
            CacheOps.Add(1, BuildTags(
                ("cache", cache),
                ("operation", operation),
                ("result", result)));

            CacheDuration.Record(durationMs, BuildTags(
                ("cache", cache),
                ("operation", operation),
                ("result", result)));
        }

        public static void RecordError(string source, string errorType)
        {
            ErrorsTotal.Add(1, BuildTags(
                ("source", source),
                ("error_type", errorType)));
            RegisterError(source);
        }

        private static TagList BuildTags(params (string Key, object? Value)[] values)
        {
            var tags = new TagList();
            foreach (var (key, value) in values)
            {
                if (!string.IsNullOrWhiteSpace(key) && value is not null)
                    tags.Add(key, value);
            }

            return tags;
        }

        private static void RegisterComponent(string component, double weight, double errorPenaltyPercent)
        {
            var normalizedComponent = NormalizeComponent(component);
            lock (ComponentSync)
            {
                if (!Components.TryGetValue(normalizedComponent, out var state))
                {
                    Components[normalizedComponent] = new ComponentHealthState(
                        normalizedComponent,
                        Math.Clamp(weight, 0, 100),
                        Math.Max(errorPenaltyPercent, 0));
                    return;
                }

                state.WeightPercent = Math.Clamp(weight, 0, 100);
                state.ErrorPenaltyPercent = Math.Max(errorPenaltyPercent, 0);
            }
        }

        private static void RegisterError(string component)
        {
            var normalizedComponent = NormalizeComponent(component);
            lock (ComponentSync)
            {
                if (!Components.TryGetValue(normalizedComponent, out var state))
                {
                    state = new ComponentHealthState(
                        normalizedComponent,
                        DefaultWeightFor(normalizedComponent),
                        15);
                    Components[normalizedComponent] = state;
                }

                state.ErrorEvents.Add(DateTimeOffset.UtcNow);
                TrimOldErrors(state, DateTimeOffset.UtcNow);
            }
        }

        private static IEnumerable<Measurement<double>> ObserveComponentWeight()
        {
            foreach (var state in SnapshotComponents())
            {
                yield return new Measurement<double>(
                    state.WeightPercent,
                    TagsFor(state.Component, HealthBand(state.HealthPercent)));
            }
        }

        private static IEnumerable<Measurement<double>> ObserveComponentHealth()
        {
            foreach (var state in SnapshotComponents())
            {
                yield return new Measurement<double>(
                    state.HealthPercent,
                    TagsFor(state.Component, HealthBand(state.HealthPercent)));
            }
        }

        private static IEnumerable<Measurement<long>> ObserveComponentStatus()
        {
            foreach (var state in SnapshotComponents())
            {
                yield return new Measurement<long>(
                    HealthStatus(state.HealthPercent),
                    TagsFor(state.Component, HealthBand(state.HealthPercent)));
            }
        }

        private static IEnumerable<Measurement<long>> ObserveComponentRecentErrors()
        {
            foreach (var state in SnapshotComponents())
            {
                yield return new Measurement<long>(
                    state.RecentErrors,
                    TagsFor(state.Component, HealthBand(state.HealthPercent)));
            }
        }

        private static List<ComponentHealthSnapshot> SnapshotComponents()
        {
            var now = DateTimeOffset.UtcNow;
            lock (ComponentSync)
            {
                foreach (var state in Components.Values)
                    TrimOldErrors(state, now);

                return Components.Values
                    .Select(state =>
                    {
                        var recentErrors = state.ErrorEvents.Count;
                        var health = Math.Clamp(100 - (recentErrors * state.ErrorPenaltyPercent), 0, 100);
                        return new ComponentHealthSnapshot(
                            state.Component,
                            state.WeightPercent,
                            health,
                            recentErrors);
                    })
                    .ToList();
            }
        }

        private static void TrimOldErrors(ComponentHealthState state, DateTimeOffset now)
        {
            var cutoff = now.AddMinutes(-5);
            state.ErrorEvents.RemoveAll(ts => ts < cutoff);
        }

        private static KeyValuePair<string, object?>[] TagsFor(string component, string healthBand) =>
            new[]
            {
                new KeyValuePair<string, object?>("component", component),
                new KeyValuePair<string, object?>("health_band", healthBand),
            };

        private static string HealthBand(double healthPercent) =>
            healthPercent >= 90 ? "green" :
            healthPercent >= 60 ? "yellow" :
            "red";

        private static long HealthStatus(double healthPercent) =>
            healthPercent >= 90 ? 2 :
            healthPercent >= 60 ? 1 :
            0;

        private static string NormalizeComponent(string component) =>
            string.IsNullOrWhiteSpace(component) ? "unknown" : component.Trim();

        private static double DefaultWeightFor(string component) =>
            NormalizeComponent(component) switch
            {
                "api-gateway" => 25,
                "legacy-sync-worker" => 25,
                "read-model-projection" => 25,
                "rx-ui" => 15,
                "rx-loadgen" => 10,
                _ => 10,
            };

        private static double ReadDouble(string? value, double fallback) =>
            double.TryParse(value, out var parsed) ? parsed : fallback;

        private sealed class ComponentHealthState
        {
            public ComponentHealthState(string component, double weightPercent, double errorPenaltyPercent)
            {
                Component = component;
                WeightPercent = weightPercent;
                ErrorPenaltyPercent = errorPenaltyPercent;
            }

            public string Component { get; }
            public double WeightPercent { get; set; }
            public double ErrorPenaltyPercent { get; set; }
            public List<DateTimeOffset> ErrorEvents { get; } = new();
        }

        private sealed record ComponentHealthSnapshot(
            string Component,
            double WeightPercent,
            double HealthPercent,
            long RecentErrors);
    }
}
