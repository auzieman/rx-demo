# BKC Pipeline: k3s Host Telemetry

This is the intended BKC SSH lane for making the k3s nodes visible in the
shared Grafana/Prometheus stack.

Targets:

- `root@192.168.1.14` / `kube1.lab.auzietek.com`
- `root@192.168.1.59` / `kube2.lab.auzietek.com`
- `root@swarm1.lab.auzietek.com` for Prometheus config and service refresh

Stages:

1. `verify-k3s`
   Run on kube1:
   ```bash
   k3s kubectl get nodes -o wide
   ```

2. `apply-host-telemetry`
   Apply `k8s/observability/k3s-host-telemetry.yaml` through kube1:
   ```bash
   k3s kubectl apply -f /tmp/k3s-host-telemetry.yaml
   k3s kubectl -n rx-observability rollout status ds/telegraf-k3s-host --timeout=180s
   k3s kubectl -n rx-observability rollout status ds/cadvisor-k3s --timeout=180s
   ```

3. `open-firewall`
   Run on kube1 and kube2:
   ```bash
   firewall-cmd --add-port=9273/tcp --add-port=18080/tcp --permanent || true
   firewall-cmd --reload || true
   ```

4. `prometheus-targets`
   Ensure `/srv/stacks/monitoring/prometheus.yml` on swarm1 contains:
   ```yaml
     - job_name: k3s-telegraf-hosts
       static_configs:
         - targets:
             - '192.168.1.14:9273'
             - '192.168.1.59:9273'

     - job_name: k3s-cadvisor
       static_configs:
         - targets:
             - '192.168.1.14:18080'
             - '192.168.1.59:18080'
   ```
   Then refresh Prometheus:
   ```bash
   docker service update --force monitoring_prometheus
   ```

5. `scrape-validate`
   Run on swarm1:
   ```bash
   curl -fsS http://127.0.0.1:9090/api/v1/targets \
     | jq -r '.data.activeTargets[]
       | select(.labels.job=="k3s-telegraf-hosts" or .labels.job=="k3s-cadvisor")
       | [.labels.job,.labels.instance,.health,.lastError] | @tsv' \
     | sort
   ```

Expected result:

```text
k3s-cadvisor        192.168.1.14:18080    up
k3s-cadvisor        192.168.1.59:18080    up
k3s-telegraf-hosts  192.168.1.14:9273     up
k3s-telegraf-hosts  192.168.1.59:9273     up
```
