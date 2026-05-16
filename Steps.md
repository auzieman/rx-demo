# rx-demo Quick Steps

## Compose

```bash
cp .env.sample .env
# Fill RABBITMQ_DEFAULT_USER, RABBITMQ_DEFAULT_PASS, and SA_PASSWORD in .env.
docker compose up -d rabbitmq redis mssql otel-collector api-gateway legacy-sync-worker read-model-projection rx-ui
curl -fsS http://localhost:8080/healthz
curl -fsS http://localhost:8081/healthz
```

## Exercise The Flow

```bash
RX_ID="RX-$(date +%s)"
curl -fsS "http://localhost:8080/prescriptions/${RX_ID}"
curl -fsS -X POST "http://localhost:8080/prescriptions/${RX_ID}/approve" \
  -H "Content-Type: application/json" \
  -d '{"approvedBy":"demo","notes":"rx-demo"}'
curl -fsS "http://localhost:8080/prescriptions/${RX_ID}"
```

## Load Generator

```bash
docker compose --profile load up -d loadgen
docker compose logs -f loadgen
```

## Kubernetes

```bash
tools/build-and-push.sh
kubectl create namespace rx-demo
kubectl -n rx-demo create secret generic rx-demo-secrets \
  --from-literal=SA_PASSWORD='<local-sql-password>' \
  --from-literal=RABBITMQ_DEFAULT_USER='<local-user>' \
  --from-literal=RABBITMQ_DEFAULT_PASS='<local-password>'
kubectl apply -k k8s/overlays/lab
kubectl -n rx-demo get pods
```
