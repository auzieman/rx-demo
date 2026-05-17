# Collector Exporter Examples

These files are reference snippets for sending the same rx-demo telemetry to
commercial or enterprise observability backends. They are not mounted by
`docker-compose.yml` and are not used by the Kubernetes manifests.

Use them as copy-in examples when adapting `collector/otel-collector.yaml` or
`k8s/base/otel-collector.yaml`.

Expected secret values:

- `DYNATRACE_OTLP_ENDPOINT`: Dynatrace OTLP HTTP endpoint, usually ending in
  `/api/v2/otlp`
- `DYNATRACE_API_TOKEN`: token with OTLP ingest permissions
- `ELASTIC_ENDPOINT`: Elasticsearch HTTPS endpoint
- `ELASTIC_API_KEY`: Elasticsearch API key
- `DATADOG_API_KEY`: Datadog API key
- `DATADOG_SITE`: Datadog site such as `datadoghq.com` or `us3.datadoghq.com`

The examples keep the local `debug` exporter available during rollout. Remove
it when the backend path is proven and volume is understood.
