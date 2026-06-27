# rx-demo Docker Compose and k3s Parity Checklist

Use this checklist to prove the k3s deployment produces the same functional
outcome as the local Docker Compose deployment.

The goal is not identical infrastructure. The goal is equivalent app behavior:

- UI responds.
- API responds.
- RabbitMQ command/event flow works.
- SQL Server writes succeed.
- Redis read model updates.
- OTel metrics/logs/traces are emitted.

## Runtime Map

| Concern | Docker Compose | k3s |
| --- | --- | --- |
| API | `api-gateway`, port `8080` | `deploy/api-gateway`, NodePort `30081` |
| UI | `rx-ui`, port `8081` | `deploy/rx-ui`, NodePort `30080` |
| SQL Server | `mssql` volume `mssql-data` | `statefulset/mssql`, PVC `mssqldata` |
| RabbitMQ | `rabbitmq` volume `rabbitmq-data` | `statefulset/rabbitmq` |
| Redis | `redis` volume `redis-data` | `deployment/redis` |
| OTel collector | `otel-collector`, port `9464` | `deploy/otel-collector`, NodePort `30964` |
| Loadgen | Compose `load` profile | `job/loadgen` or steady `deploy/loadgen` |

## Environment

Set endpoints before running checks:

```bash
export RX_COMPOSE_API='http://localhost:8080'
export RX_COMPOSE_UI='http://localhost:8081'
export RX_COMPOSE_OTEL='http://localhost:9464'

export RX_K3S_API='http://<k3s-node>:30081'
export RX_K3S_UI='http://<k3s-node>:30080'
export RX_K3S_OTEL='http://<k3s-node>:30964'

export DEMO_REGISTRY='swarm1.lab.auzietek.com:5001/rx-demo'
```

Do not put real credentials in this file. Runtime secrets belong in `.env`,
Kubernetes secrets, or the local shell.

## Docker Compose Baseline

Start dependencies and apps:

```bash
cd /home/auzieman/Projects/rx-demo
docker compose up -d rabbitmq redis mssql otel-collector api-gateway legacy-sync-worker read-model-projection rx-ui
```

Check health:

```bash
curl -fsS "$RX_COMPOSE_API/healthz"
curl -fsS "$RX_COMPOSE_API/readyz"
curl -fsS "$RX_COMPOSE_UI/healthz"
```

Exercise one prescription:

```bash
RX_ID="RX-COMPOSE-$(date +%s)"
curl -fsS "$RX_COMPOSE_API/prescriptions/${RX_ID}"
curl -fsS -X POST "$RX_COMPOSE_API/prescriptions/${RX_ID}/approve" \
  -H "Content-Type: application/json" \
  -d '{"approvedBy":"demo","notes":"compose-parity"}'
sleep 3
curl -fsS "$RX_COMPOSE_API/prescriptions/${RX_ID}"
```

Check telemetry:

```bash
curl -fsS "$RX_COMPOSE_OTEL/metrics" | grep -E 'rx_|otelcol_' | head
docker compose logs --tail 40 api-gateway
docker compose logs --tail 40 legacy-sync-worker
docker compose logs --tail 40 read-model-projection
```

Expected outcome:

- API health and readiness return success.
- UI health returns success.
- Approval request returns success.
- Readback shows the prescription exists with updated status/version.
- Collector metrics include rx-demo or OTel collector series.

## k3s Deployment

Prepare a commit-tagged image set:

```bash
cd /home/auzieman/Projects/rx-demo
export DEMO_TAG="$(git rev-parse --short HEAD)"
PUSH=1 REGISTRY="$DEMO_REGISTRY" TAG="$DEMO_TAG" tools/build-and-push.sh
```

Apply the lab overlay:

```bash
kubectl apply -k k8s/overlays/lab
```

Pin workloads to the registry image tag:

```bash
kubectl -n rx-demo set image deploy/api-gateway \
  api-gateway="$DEMO_REGISTRY/api-gateway:$DEMO_TAG"
kubectl -n rx-demo set image deploy/legacy-sync-worker \
  worker="$DEMO_REGISTRY/legacy-sync-worker:$DEMO_TAG"
kubectl -n rx-demo set image deploy/read-model-projection \
  worker="$DEMO_REGISTRY/read-model-projection:$DEMO_TAG"
kubectl -n rx-demo set image deploy/rx-ui \
  rx-ui="$DEMO_REGISTRY/rx-ui:$DEMO_TAG"
```

Wait for rollout:

```bash
kubectl -n rx-demo rollout status deploy/api-gateway --timeout=180s
kubectl -n rx-demo rollout status deploy/legacy-sync-worker --timeout=180s
kubectl -n rx-demo rollout status deploy/read-model-projection --timeout=180s
kubectl -n rx-demo rollout status deploy/rx-ui --timeout=180s
kubectl -n rx-demo get pods -o wide
```

## k3s Parity Checks

Check health:

```bash
curl -fsS "$RX_K3S_API/healthz"
curl -fsS "$RX_K3S_API/readyz"
curl -fsS "$RX_K3S_UI/healthz"
```

Exercise one prescription:

```bash
RX_ID="RX-K3S-$(date +%s)"
curl -fsS "$RX_K3S_API/prescriptions/${RX_ID}"
curl -fsS -X POST "$RX_K3S_API/prescriptions/${RX_ID}/approve" \
  -H "Content-Type: application/json" \
  -d '{"approvedBy":"demo","notes":"k3s-parity"}'
sleep 3
curl -fsS "$RX_K3S_API/prescriptions/${RX_ID}"
```

Check telemetry:

```bash
curl -fsS "$RX_K3S_OTEL/metrics" | grep -E 'rx_|otelcol_' | head
kubectl -n rx-demo logs deploy/api-gateway --tail=40
kubectl -n rx-demo logs deploy/legacy-sync-worker --tail=40
kubectl -n rx-demo logs deploy/read-model-projection --tail=40
```

Expected outcome:

- Health and readiness are successful through NodePort.
- The same prescription approve/readback flow works.
- Logs show API, worker, and projection activity.
- Collector metrics are present.
- Grafana dashboards move after traffic.

## BKC Pipeline Mapping

Open these folder-backed pipelines in the editor and Web UI:

- `/home/auzieman/Projects/BlackKnightController/dictionaries/pipelines/Rx_Demo_K3s_Registry_Preflight/pipeline.json`
- `/home/auzieman/Projects/BlackKnightController/dictionaries/pipelines/Rx_Demo_K3s_Deploy/pipeline.json`
- `/home/auzieman/Projects/BlackKnightController/dictionaries/pipelines/Rx_Demo_K3s_Redeploy_From_Git/pipeline.json`
- `/home/auzieman/Projects/BlackKnightController/dictionaries/pipelines/Rx_Demo_K3s_Undeploy/pipeline.json`

Narration point:

> Compose proves the app locally. The k3s pipeline proves the same behavior
> after images move through a registry and Kubernetes performs the rollout.

## Undeploy

Use this after a take only when the namespace is no longer needed:

```bash
kubectl -n rx-demo get all,pvc,secret,configmap -o wide || true
kubectl delete -k k8s/overlays/lab --ignore-not-found=true
kubectl delete namespace rx-demo --ignore-not-found=true --timeout=180s
! kubectl get namespace rx-demo
```

Leave the image registry and shared observability stack running for additional
takes unless the full lab is being cleaned.
