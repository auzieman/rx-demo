# Validate rx-demo Locally

1. Start the stack:

   ```bash
   cp .env.sample .env
   # Fill RABBITMQ_DEFAULT_USER, RABBITMQ_DEFAULT_PASS, and SA_PASSWORD in .env.
   docker compose up -d rabbitmq redis mssql otel-collector api-gateway legacy-sync-worker read-model-projection rx-ui
   ```

2. Check health:

   ```bash
   curl -fsS http://localhost:8080/healthz
   curl -fsS http://localhost:8081/healthz
   ```

3. Exercise a prescription:

   ```bash
   RX_ID="RX-$(date +%s)"
   curl -fsS "http://localhost:8080/prescriptions/${RX_ID}"
   curl -fsS -X POST "http://localhost:8080/prescriptions/${RX_ID}/approve" \
     -H "Content-Type: application/json" \
     -d '{"approvedBy":"demo","notes":"validation"}'
   sleep 3
   curl -fsS "http://localhost:8080/prescriptions/${RX_ID}"
   ```

4. Check JSON logs and OTEL collector:

   ```bash
   docker compose logs --tail 50 api-gateway
   docker compose logs --tail 50 legacy-sync-worker
   docker compose logs --tail 50 read-model-projection
   docker compose logs --tail 50 otel-collector
   curl -fsS http://localhost:9464/metrics | head
   ```

By default, OTEL logs are forwarded to the Loki endpoint configured in `.env`.
