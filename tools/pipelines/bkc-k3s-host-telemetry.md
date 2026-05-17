# BKC Pipeline: k3s Lab Housekeeping

This is the intended BKC SSH lane for keeping the k3s lab nodes aligned with
the shared storage and observability stack.

Targets:

- `root@192.168.1.14` / `kube1.lab.auzietek.com`
- `root@192.168.1.59` / `kube2.lab.auzietek.com`
- `root@swarm1.lab.auzietek.com` for Prometheus config and service refresh

Stages:

1. `verify-k3s`
   Run on kube1:
   ```bash
   k3s kubectl get nodes -o wide
   k3s kubectl wait --for=condition=Ready nodes --all --timeout=90s
   ```

2. `nfs-projects`
   Mount shared project paths on kube1 and kube2:
   ```text
   192.168.1.10:/srv/nfs/swarm/shared -> /mnt/swarm/shared
   192.168.1.10:/srv/nfs/swarm/tabor-linux-forge -> /mnt/swarm/tabor-linux-forge
   192.168.1.10:/srv/nfs/swarm/blackknightcontroller -> /mnt/swarm/blackknightcontroller
   ```

3. `apply-host-telemetry`
   Apply `k8s/observability/k3s-host-telemetry.yaml` through kube1:
   ```bash
   k3s kubectl apply -f /tmp/k3s-host-telemetry.yaml
   k3s kubectl -n rx-observability rollout status ds/telegraf-k3s-host --timeout=180s
   k3s kubectl -n rx-observability rollout status ds/cadvisor-k3s --timeout=180s
   ```

4. `apply-loki-logs`
   Apply `k8s/observability/k3s-loki-logs.yaml` through kube1. This deploys
   `promtail-k3s` and ships host logs as `job="k3s-hostlogs"` and pod logs as
   `job="k3s-pods"`.

5. `loadgen-steady`
   Apply `k8s/observability/loadgen-deployment.yaml` through kube1. This keeps
   `rx-loadgen` running as a Deployment instead of a one-shot Job.

6. `open-firewall`
   Run on kube1 and kube2:
   ```bash
   firewall-cmd --add-port=9273/tcp --add-port=18080/tcp --permanent || true
   firewall-cmd --reload || true
   ```

7. `prometheus-targets`
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

8. `scrape-validate`
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
