# Kyverno Policies

These policies are optional audit-mode examples for platform clusters. They
are intentionally scoped to the `rx-demo` namespace and start in `Audit` mode
so they can be evaluated before a platform team switches them to `Enforce`.

Included checks:

- images should come from the approved Harbor registry
- pods should avoid service account token mounts
- pods should use `RuntimeDefault` seccomp
- containers should drop Linux capabilities and disallow privilege escalation
- containers should declare CPU and memory requests and limits

Apply after Kyverno is installed:

```bash
kubectl apply -k k8s/policies/kyverno
```
