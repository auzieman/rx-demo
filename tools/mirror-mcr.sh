#!/usr/bin/env bash
set -euo pipefail

# Optional helper for disconnected labs. Set REGISTRY to your local registry prefix,
# for example REGISTRY=registry.example.local/rx-demo.
REGISTRY="${REGISTRY:-rx-demo}"

mirror() {
  local source="$1"
  local target="$2"
  docker pull "${source}"
  docker tag "${source}" "${REGISTRY}/${target}"
  if [[ "${PUSH:-0}" == "1" ]]; then
    docker push "${REGISTRY}/${target}"
  fi
}

mirror mcr.microsoft.com/mssql/server:2022-latest mssql-server:2022-latest
mirror rabbitmq:3.12-management rabbitmq:3.12-management
mirror redis:7-alpine redis:7-alpine
mirror otel/opentelemetry-collector-contrib:0.103.0 opentelemetry-collector-contrib:0.103.0
