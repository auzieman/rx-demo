# Tanzu / Harbor Overlay

This overlay prepares the demo for a policy-aware Kubernetes environment such
as VMware Tanzu with Harbor as the container registry.

It keeps the lab overlay separate from platform-facing choices:

- Harbor image names for app, RabbitMQ, Redis, SQL Server, and OTel Collector
- `harbor-registry` image pull secret on all workloads
- internal `ClusterIP` service for the OTel collector instead of lab NodePort
- `tanzu-storage-policy` storage class for SQL Server data
- CPU and memory requests and limits
- restricted-style security context on application containers
- namespace labels for Pod Security admission audit/warn signals

Render with environment-specific values:

```bash
REGISTRY=harbor.example.com PROJECT=rx-demo TAG=latest \
  STORAGE_CLASS=tanzu-storage-policy PULL_SECRET=harbor-registry \
  tools/render-tanzu.sh
```

Create the `rx-demo-secrets` runtime secret and registry pull secret outside
the repo before applying the rendered manifest.
