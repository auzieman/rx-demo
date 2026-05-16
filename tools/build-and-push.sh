#!/usr/bin/env bash
set -euo pipefail

TAG="${TAG:-latest}"
DOTNET_VERSION="${DOTNET_VERSION:-10.0}"
REGISTRY="${REGISTRY:-rx-demo}"

build_and_push() {
  local name="$1"
  local dockerfile="$2"

  docker build \
    -f "${dockerfile}" \
    -t "${REGISTRY}/${name}:${TAG}" \
    --build-arg "DOTNET_VERSION=${DOTNET_VERSION}" \
    .

  if [[ "${PUSH:-0}" == "1" ]]; then
    docker push "${REGISTRY}/${name}:${TAG}"
  fi
}

build_and_push rx-ui src/rx-ui/Rx.Ui/Dockerfile
build_and_push api-gateway src/api-gateway/Dockerfile
build_and_push legacy-sync-worker src/legacy-sync-worker/Dockerfile
build_and_push read-model-projection src/read-model-projection/Dockerfile
build_and_push loadgen src/loadgen/Dockerfile
