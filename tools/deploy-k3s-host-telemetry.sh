#!/usr/bin/env bash
set -euo pipefail

KUBE_SSH_HOST="${KUBE_SSH_HOST:-root@192.168.1.14}"
PROM_SSH_HOST="${PROM_SSH_HOST:-root@swarm1.lab.auzietek.com}"
KUBE1_TARGET="${KUBE1_TARGET:-192.168.1.14}"
KUBE2_TARGET="${KUBE2_TARGET:-192.168.1.59}"
PROMETHEUS_CONFIG="${PROMETHEUS_CONFIG:-/srv/stacks/monitoring/prometheus.yml}"
MANIFEST="${MANIFEST:-k8s/observability/k3s-host-telemetry.yaml}"
LOGS_MANIFEST="${LOGS_MANIFEST:-k8s/observability/k3s-loki-logs.yaml}"
LOADGEN_MANIFEST="${LOADGEN_MANIFEST:-k8s/observability/loadgen-deployment.yaml}"

if [[ ! -f "${MANIFEST}" ]]; then
  echo "Manifest not found: ${MANIFEST}" >&2
  exit 2
fi
if [[ ! -f "${LOGS_MANIFEST}" ]]; then
  echo "Logs manifest not found: ${LOGS_MANIFEST}" >&2
  exit 2
fi
if [[ ! -f "${LOADGEN_MANIFEST}" ]]; then
  echo "Loadgen manifest not found: ${LOADGEN_MANIFEST}" >&2
  exit 2
fi

ssh "${KUBE_SSH_HOST}" 'cat >/tmp/k3s-host-telemetry.yaml && k3s kubectl apply -f /tmp/k3s-host-telemetry.yaml' < "${MANIFEST}"
ssh "${KUBE_SSH_HOST}" 'k3s kubectl -n rx-observability rollout status ds/telegraf-k3s-host --timeout=180s && k3s kubectl -n rx-observability rollout status ds/cadvisor-k3s --timeout=180s'
ssh "${KUBE_SSH_HOST}" 'cat >/tmp/k3s-loki-logs.yaml && k3s kubectl apply -f /tmp/k3s-loki-logs.yaml' < "${LOGS_MANIFEST}"
ssh "${KUBE_SSH_HOST}" 'k3s kubectl -n rx-observability rollout status ds/promtail-k3s --timeout=180s'
ssh "${KUBE_SSH_HOST}" 'cat >/tmp/rx-loadgen-deployment.yaml && k3s kubectl apply -f /tmp/rx-loadgen-deployment.yaml' < "${LOADGEN_MANIFEST}"
ssh "${KUBE_SSH_HOST}" 'k3s kubectl -n rx-demo rollout status deploy/loadgen --timeout=180s'

for node in "${KUBE1_TARGET}" "${KUBE2_TARGET}"; do
  ssh "root@${node}" 'firewall-cmd --add-port=9273/tcp --add-port=18080/tcp --permanent 2>/dev/null || true; firewall-cmd --reload 2>/dev/null || true'
done

ssh "${PROM_SSH_HOST}" PROMETHEUS_CONFIG="${PROMETHEUS_CONFIG}" KUBE1_TARGET="${KUBE1_TARGET}" KUBE2_TARGET="${KUBE2_TARGET}" 'bash -s' <<'REMOTE'
set -euo pipefail
config="${PROMETHEUS_CONFIG}"
backup="${config}.bak.$(date +%Y%m%d%H%M%S)"
cp "${config}" "${backup}"

python3 - "$config" "$KUBE1_TARGET" "$KUBE2_TARGET" <<'PY'
from pathlib import Path
import sys
path = Path(sys.argv[1])
kube1 = sys.argv[2]
kube2 = sys.argv[3]
text = path.read_text(encoding="utf-8")
marker = "  - job_name: k3s-telegraf-hosts\n"
block = f"""
  - job_name: k3s-telegraf-hosts
    static_configs:
      - targets:
          - '{kube1}:9273'
          - '{kube2}:9273'

  - job_name: k3s-cadvisor
    static_configs:
      - targets:
          - '{kube1}:18080'
          - '{kube2}:18080'
"""
if marker not in text:
    path.write_text(text.rstrip() + "\n" + block, encoding="utf-8")
PY

docker service update --force monitoring_prometheus >/dev/null
for _ in $(seq 1 30); do
  curl -fsS http://127.0.0.1:9090/-/healthy >/dev/null && break
  sleep 2
done
curl -fsS http://127.0.0.1:9090/api/v1/targets \
  | jq -r '.data.activeTargets[] | select(.labels.job=="k3s-telegraf-hosts" or .labels.job=="k3s-cadvisor") | [.labels.job,.labels.instance,.health] | @tsv' \
  | sort
REMOTE
