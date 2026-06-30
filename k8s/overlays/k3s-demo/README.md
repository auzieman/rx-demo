# k3s Demo Overlay

This overlay deploys the full recording path into k3s:

- rx-demo application stack in `rx-demo`
- OTel collector in `rx-demo`
- k3s host telemetry and promtail in `rx-observability`
- Prometheus, Loki, Tempo, and Grafana in `rx-observability`
- steady loadgen deployment for live dashboard movement
- NodePort access for rx-ui, api-gateway, Grafana, Prometheus, Loki, and Tempo

Ingress is intentionally not required because the current lab k3s cluster does
not expose an IngressClass. Use these demo URLs from any k3s node:

```text
rx-ui:      http://<k3s-node>:30080
api:        http://<k3s-node>:30081
grafana:    http://<k3s-node>:30300
prometheus: http://<k3s-node>:30090
loki:       http://<k3s-node>:30100
tempo:      http://<k3s-node>:30200
```

Apply:

```bash
kubectl apply -k k8s/overlays/k3s-demo
```

The overlay expects `rx-demo-secrets` to exist in the `rx-demo` namespace before
application pods start.
