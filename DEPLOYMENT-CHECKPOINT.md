# Deployment Checkpoint

This branch is for proving the direct RabbitMQ refactor in the lab and then
refreshing dashboards around the new fault and health behavior.

## Current Main Checkpoint

`main` contains the clean code checkpoint:

- .NET 10 solution files: `rx-demo.sln` and `rx-demo.slnx`
- Direct RabbitMQ publisher and consumers
- Transport-neutral message validation
- Updated load-generation fault defaults
- Tempo local-blocks configuration for TraceQL metric panels
- Direct RabbitMQ declares the existing lab exchanges as `fanout` for
  compatibility with the original MassTransit-created broker topology.

Build verification:

```bash
PATH="$HOME/.dotnet:$PATH" dotnet build rx-demo.sln
```

Container build path:

```bash
REGISTRY=rx-demo TAG=latest tools/build-and-push.sh
```

## Lab Deployment Order

1. Build the five images with `tools/build-and-push.sh`.
2. Import or push images to the k3s nodes according to the active lab path.
3. Apply the lab overlay:

   ```bash
   kubectl apply -k k8s/overlays/lab
   kubectl -n rx-demo rollout status deploy/api-gateway
   kubectl -n rx-demo rollout status deploy/legacy-sync-worker
   kubectl -n rx-demo rollout status deploy/read-model-projection
   kubectl -n rx-demo rollout status deploy/rx-ui
   ```

4. Run a bounded load job:

   ```bash
   kubectl -n rx-demo delete job loadgen --ignore-not-found
   kubectl apply -k k8s/overlays/lab
   kubectl -n rx-demo logs -f job/loadgen
   ```

## Validation Probes

API readiness should include Redis and RabbitMQ:

```bash
curl -fsS http://kube1.lab.auzietek.com:30081/readyz
```

Current lab discovery note:

- Intended names: `kube1.lab.auzietek.com`, `kube2.lab.auzietek.com`
- DNS currently resolves them as `192.168.1.24` and `192.168.1.19` from this
  workstation, but those addresses do not answer.
- Live NodePort/API endpoints were found at `192.168.1.14` and `192.168.1.59`.
- HTTP validation works on both live addresses, but SSH key access is denied for
  `root` and `auzieman`, so image import and `kubectl` rollout actions are
  blocked until DNS and SSH access are aligned.

Exercise one prescription end to end:

```bash
RX_ID="RX-CHECK-$(date +%s)"
curl -fsS "http://kube1.lab.auzietek.com:30081/prescriptions/${RX_ID}"
curl -fsS -X POST "http://kube1.lab.auzietek.com:30081/prescriptions/${RX_ID}/approve" \
  -H "Content-Type: application/json" \
  -d '{"approvedBy":"demo","notes":"deployment-check"}'
sleep 3
curl -fsS "http://kube1.lab.auzietek.com:30081/prescriptions/${RX_ID}"
```

Expected observability signals:

- `rx.message.publish_total` on `rx.commands` and `rx.events`
- `rx.queue.messages_total` on `rx.commands` and `rx.events`
- `rx.errors_total` moves during moderate/aggressive load profiles
- `rx.component.health_percent` moves yellow/red when recent faults are active
- Tempo TraceQL metrics return `api-gateway`, `legacy-sync-worker`,
  `read-model-projection`, `rx-ui`, and `rx-loadgen` after fresh traffic

## Dashboard Backlog

Short-term dashboard updates:

- Start with Grafmaid for the executive service map. The shared lab Grafana
  already has `neildengg-grafmaid-panel` installed, and the repo now includes
  `collector/rx-executive-flow-grafmaid-dashboard.json`.
- Use `collector/rx-grafmaid-probe-dashboard.json` to validate query-driven
  Mermaid values and threshold colors before polishing the executive view.
- Use `collector/rx-traffic-map-grafmaid-dashboard.json` for a route latency
  traffic map across UI, API, RabbitMQ command/event queues, SQL, and Redis.
- Add a direct RabbitMQ flow panel set: command publish, command consume,
  event publish, event consume, and projection cache writes.
- Make executive health visibly explain its weighting: component weight,
  current health, recent error count, and fault-mode context.
- Add a fault drilldown row: API faults, worker faults, projection/cache faults,
  and loadgen fault selection.
- Add a "lab story" dashboard that is safe for executive viewing: platform
  status, demo service health, current traffic, and top degraded component.

## Platform Deployment Backlog

- Keep `k8s/overlays/lab` focused on the current k3s lab and use
  `k8s/overlays/tanzu` for Harbor-backed, policy-aware Kubernetes clusters.
- Tune the Tanzu overlay's Harbor registry, storage class, pull secret, and
  observability endpoints per target cluster before applying.
- Start Kyverno examples in audit mode, review policy reports, then promote
  selected checks to enforce mode when the app and dependency images comply.

## Telemetry Notes

- Dashboard PromQL now matches the live k3s metric shape: queue labels use
  `queue`, API success uses `result="ok"`, and loadgen latency uses
  `rx_synthetic_duration_ms_milliseconds_*`.
- Loki labels in the shared lab are service-name based for rx-demo
  (`service_name="rx/api-gateway"` and similar), while local Docker Promtail
  can still emit `compose_service` labels.
- Collector exporter snippets for Dynatrace, Elasticsearch, and Datadog are
  examples only and are not mounted into the active lab collector.

Flow/Grafmaid ideas:

- Use the README Mermaid graph as the source-of-truth service map.
- Add an executive summary graph that groups UI/API, command processing,
  persistence, projection, and observability.
- Later, add a BlackKnight/lab graph that shows cluster/node relationships
  without embedding secrets or host-specific credentials.

Infrastructure telemetry:

- Swarm host/node telemetry should start with Docker and system stats.
- Telegraf is a reasonable first pass for Swarm nodes because it can emit
  host, Docker, disk, network, and process metrics without changing app code.
- k3s can stay app-focused for this pass; node telemetry can be added after
  the direct RabbitMQ deployment is verified.
