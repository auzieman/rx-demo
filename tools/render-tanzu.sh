#!/usr/bin/env bash
set -euo pipefail

REGISTRY="${REGISTRY:-harbor.example.com}"
PROJECT="${PROJECT:-rx-demo}"
TAG="${TAG:-latest}"
STORAGE_CLASS="${STORAGE_CLASS:-tanzu-storage-policy}"
PULL_SECRET="${PULL_SECRET:-harbor-registry}"
OVERLAY="${OVERLAY:-k8s/overlays/tanzu}"
if [[ -n "${KUBECTL:-}" ]]; then
  KUSTOMIZE=("${KUBECTL}" kustomize)
elif command -v kubectl >/dev/null 2>&1; then
  KUSTOMIZE=(kubectl kustomize)
elif command -v k3s >/dev/null 2>&1; then
  KUSTOMIZE=(k3s kubectl kustomize)
elif command -v kustomize >/dev/null 2>&1; then
  KUSTOMIZE=(kustomize build)
else
  echo "kubectl, k3s, or kustomize is required to render the Tanzu overlay" >&2
  exit 127
fi

tmpdir="$(mktemp -d k8s/overlays/.tanzu-render.XXXXXX)"
trap 'rm -rf "${tmpdir}"' EXIT

cp -R "${OVERLAY}/." "${tmpdir}/"

replace() {
  local file="$1"
  local from="$2"
  local to="$3"
  sed -i "s|${from}|${to}|g" "${file}"
}

replace "${tmpdir}/kustomization.yaml" "harbor.example.com/rx-demo" "${REGISTRY}/${PROJECT}"
replace "${tmpdir}/kustomization.yaml" "harbor.example.com/library" "${REGISTRY}/library"
replace "${tmpdir}/kustomization.yaml" "harbor.example.com/mcr" "${REGISTRY}/mcr"
replace "${tmpdir}/kustomization.yaml" "harbor.example.com/otel" "${REGISTRY}/otel"
replace "${tmpdir}/kustomization.yaml" "newTag: latest" "newTag: ${TAG}"
replace "${tmpdir}/mssql-storage-patch.yaml" "tanzu-storage-policy" "${STORAGE_CLASS}"
replace "${tmpdir}/image-pull-secret-patch.yaml" "harbor-registry" "${PULL_SECRET}"

"${KUSTOMIZE[@]}" "${tmpdir}"
