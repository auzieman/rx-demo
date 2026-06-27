# BKC + rx-demo Video Rehearsal Runbook

This runbook is for a live demo recording where Black Knight Controller (BKC)
drives a repeatable k3s deployment loop for `rx-demo`, then a small source
change triggers a redeploy and validation pass.

The product message:

> BKC is not trying to replace ArgoCD. ArgoCD reconciles Kubernetes desired
> state. BKC records and triggers operational pipelines across Git, hosts,
> clusters, APIs, deployment actions, and validation evidence.

## Demo Story

1. Show BKC as the operator surface.
2. Validate or expand the k3s lab setup.
3. Deploy `rx-demo` to k3s through repeatable steps.
4. Watch app, host, and pipeline telemetry.
5. Make a tiny source change in the editor.
6. Commit the change and trigger a BKC redeploy pipeline.
7. Show the redeployed UI and the pipeline evidence.

## Windows To Have Ready

Use separate virtual desktops so the recording can move cleanly between views.

### Desktop 1: BKC

Open:

- BKC overview: `http://<bkc-host>:5000/`
- BKC pipelines: `http://<bkc-host>:5000/pipelines`
- BKC selected run detail after each trigger.
- Optional: BKC resource graph or inventory console.

What to highlight:

- k3s, Docker, SSH, pipeline, and integration resources are all visible from
  one surface.
- Pipeline runs have stage state and event history.
- This is operational evidence, not just a terminal transcript.

### Desktop 2: Grafana / Observability

Open:

- Grafana home: `http://<grafana-host>:3000`
- `Rx Overview`
- `Rx Service Flow`
- `Rx Executive Health`
- Optional: Prometheus targets page, Loki Explore, Tempo traces.

What to highlight:

- Before deploy: dashboards may be quiet or show older state.
- After deploy: services emit metrics/logs/traces.
- After loadgen: request rate, errors, queue activity, and health panels move.
- After redeploy: rollout should not erase observability context.

### Desktop 3: Editor

Open these files side-by-side:

- `docs/bkc-demo-video-rehearsal.md`
- `docs/kubectl-compose-parity.md`
- `Readme.md`
- `k8s/overlays/lab/kustomization.yaml`
- `k8s/base/apps.yaml`
- `src/rx-ui/Rx.Ui/Pages/Index.cshtml`
- `tools/build-and-push.sh`
- `tools/deploy-k3s-host-telemetry.sh`
- Optional BKC API reference:
  `/home/auzieman/Projects/BlackKnightController/routes/api_v1.py`
- BKC folder-backed demo pipelines:
  - `/home/auzieman/Projects/BlackKnightController/dictionaries/pipelines/Demo_Swarm_Image_Registry/pipeline.json`
  - `/home/auzieman/Projects/BlackKnightController/dictionaries/pipelines/Demo_K3s_Add_Node/pipeline.json`
  - `/home/auzieman/Projects/BlackKnightController/dictionaries/pipelines/Rx_Demo_K3s_Registry_Preflight/pipeline.json`
  - `/home/auzieman/Projects/BlackKnightController/dictionaries/pipelines/Rx_Demo_K3s_Deploy/pipeline.json`
  - `/home/auzieman/Projects/BlackKnightController/dictionaries/pipelines/Rx_Demo_K3s_Redeploy_From_Git/pipeline.json`
  - `/home/auzieman/Projects/BlackKnightController/dictionaries/pipelines/Rx_Demo_K3s_Undeploy/pipeline.json`

The live source change should be in:

```text
src/rx-ui/Rx.Ui/Pages/Index.cshtml
```

Suggested change:

```html
<h1>Prescription Demo UI</h1>
```

to:

```html
<h1>Prescription Demo UI - BKC Redeploy</h1>
```

This is visible, reversible, and low risk.

### Desktop 4: Terminal

Use one terminal per lane:

- `rx-demo` repo terminal.
- BKC API trigger terminal.
- k3s validation terminal.
- local image registry terminal.
- Optional observability validation terminal.

## Pre-Flight Checklist

Do these before recording.

### 1. Confirm Repo State

```bash
cd /home/auzieman/Projects/rx-demo
git status --short
```

Expected:

- Only intentional rehearsal docs or demo changes are present.
- No real credentials are staged or visible.

```bash
cd /home/auzieman/Projects/BlackKnightController
git status --short
```

Expected:

- README work may be present.
- Ignore unrelated untracked screenshots unless they are part of the demo.

### 2. Confirm k3s Access

From the workstation or through the k3s control node:

```bash
ssh <k3s-control-ssh> 'k3s kubectl get nodes -o wide'
ssh <k3s-control-ssh> 'k3s kubectl wait --for=condition=Ready nodes --all --timeout=90s'
```

Set the control host privately in the shell before rehearsal:

```bash
export KUBE_SSH_HOST='root@<k3s-control-ip>'
```

For the public demo, say "k3s control node" on camera rather than reading out
private hostnames or IPs.

### 3. Confirm Secrets Exist

```bash
ssh <k3s-control-ssh> 'k3s kubectl -n rx-demo get secret rx-demo-secrets'
```

If missing, create it with local demo-only values:

```bash
k3s kubectl create namespace rx-demo --dry-run=client -o yaml | k3s kubectl apply -f -
k3s kubectl -n rx-demo create secret generic rx-demo-secrets \
  --from-literal=SA_PASSWORD='<demo-sql-password>' \
  --from-literal=RABBITMQ_DEFAULT_USER='<demo-rabbit-user>' \
  --from-literal=RABBITMQ_DEFAULT_PASS='<demo-rabbit-password>'
```

Do not show real secret values on camera.

### 4. Confirm Image Registry Path

The demo should include an image distribution step. Building images on the
workstation and importing them directly into k3s works in this lab, but the more
typical path is:

```text
build image -> push registry -> k3s pulls image -> rollout validates
```

For a production-style environment, use Harbor. For this demo, the simple
service is Docker Registry / Distribution, normally run as `registry:2` on the
Docker Swarm side of the lab. K3s should consume images from the registry; it
should not serve its own images.

Deploy the registry swarm stack before recording:

```bash
cd /home/auzieman/Projects/ldap_stack/ansible
ANSIBLE_CONFIG=/srv/ansible/ansible.cfg \
  /opt/ansible-venv/bin/ansible-playbook -i inventory/lab.yml registry-stack.yml
```

Set the registry prefix privately in the shell:

```bash
export DEMO_REGISTRY='swarm1.lab.auzietek.com:5001/rx-demo'
```

If the registry uses plain HTTP, configure k3s/containerd to trust it on every
k3s node before recording:

```bash
cat >/etc/rancher/k3s/registries.yaml <<'YAML'
mirrors:
  "swarm1.lab.auzietek.com:5001":
    endpoint:
      - "http://swarm1.lab.auzietek.com:5001"
YAML

systemctl restart k3s || systemctl restart k3s-agent
```

Verify from the workstation:

```bash
curl -fsS "http://${DEMO_REGISTRY%/rx-demo}/v2/_catalog"
```

Build and push images:

```bash
cd /home/auzieman/Projects/rx-demo
PUSH=1 REGISTRY="$DEMO_REGISTRY" TAG=latest tools/build-and-push.sh
```

If Docker refuses to push to the plain-HTTP swarm registry from the workstation,
either configure the workstation Docker daemon with `insecure-registries` for
the registry endpoint, or build/push from `swarm1` against
`127.0.0.1:5001/rx-demo` and keep the Kubernetes image reference pointed at
`swarm1.lab.auzietek.com:5001/rx-demo`.

For the live redeploy, prefer a unique tag such as the commit hash. The base
manifests use `imagePullPolicy: IfNotPresent`, so reusing `latest` can leave a
node running a cached image.

For the lab overlay, either update `k8s/overlays/lab/kustomization.yaml` before
recording or apply explicit image updates after deploy:

```bash
kubectl -n rx-demo set image deploy/api-gateway \
  api-gateway="$DEMO_REGISTRY/api-gateway:latest"
kubectl -n rx-demo set image deploy/legacy-sync-worker \
  worker="$DEMO_REGISTRY/legacy-sync-worker:latest"
kubectl -n rx-demo set image deploy/read-model-projection \
  worker="$DEMO_REGISTRY/read-model-projection:latest"
kubectl -n rx-demo set image deploy/rx-ui \
  rx-ui="$DEMO_REGISTRY/rx-ui:latest"
```

Use direct k3s image import only as a fallback if the registry is not ready.

### 5. Confirm Local Build Path

The lab overlay currently references local image names such as:

```text
rx-demo/rx-ui:latest
rx-demo/api-gateway:latest
rx-demo/legacy-sync-worker:latest
rx-demo/read-model-projection:latest
rx-demo/loadgen:latest
```

Build images without pushing only when using direct import or node-local builds:

```bash
cd /home/auzieman/Projects/rx-demo
REGISTRY=rx-demo TAG=latest tools/build-and-push.sh
```

If the k3s nodes cannot see local Docker images from the workstation, choose
one of these before recording:

- Push to the `registry:2` demo registry described above.
- Set `PUSH=1 REGISTRY=<registry-prefix>` and update the lab overlay image
  names.
- Build directly on the k3s node.
- Import images into k3s/containerd as part of the deployment lane.

Do not discover this during the recording.

### 6. Apply Initial rx-demo Deployment

```bash
cd /home/auzieman/Projects/rx-demo
kubectl apply -k k8s/overlays/lab
kubectl -n rx-demo set image deploy/api-gateway api-gateway="$DEMO_REGISTRY/api-gateway:latest"
kubectl -n rx-demo set image deploy/legacy-sync-worker worker="$DEMO_REGISTRY/legacy-sync-worker:latest"
kubectl -n rx-demo set image deploy/read-model-projection worker="$DEMO_REGISTRY/read-model-projection:latest"
kubectl -n rx-demo set image deploy/rx-ui rx-ui="$DEMO_REGISTRY/rx-ui:latest"
kubectl -n rx-demo get pods
kubectl -n rx-demo rollout status deploy/api-gateway --timeout=180s
kubectl -n rx-demo rollout status deploy/legacy-sync-worker --timeout=180s
kubectl -n rx-demo rollout status deploy/read-model-projection --timeout=180s
kubectl -n rx-demo rollout status deploy/rx-ui --timeout=180s
```

If you need to run through the k3s control host:

```bash
tar -C /home/auzieman/Projects/rx-demo -cf - k8s | ssh <k3s-control-ssh> '
  rm -rf /tmp/rx-demo-k8s &&
  mkdir -p /tmp/rx-demo-k8s &&
  tar -C /tmp/rx-demo-k8s -xf - &&
  cd /tmp/rx-demo-k8s &&
  k3s kubectl apply -k k8s/overlays/lab
'
```

### 7. Apply Host Telemetry

```bash
cd /home/auzieman/Projects/rx-demo
tools/deploy-k3s-host-telemetry.sh
```

Expected Prometheus target validation should include:

```text
k3s-cadvisor        <node>:18080    up
k3s-telegraf-hosts  <node>:9273     up
```

### 8. Start or Confirm Load Generation

For Kubernetes:

```bash
kubectl -n rx-demo get deploy loadgen
kubectl -n rx-demo rollout status deploy/loadgen --timeout=180s
```

If using the one-shot job instead:

```bash
kubectl -n rx-demo get jobs
```

### 9. Confirm Useful URLs

Capture final URLs in a scratch note before recording:

```text
BKC:
Grafana:
Prometheus:
rx-ui:
api-gateway:
```

If NodePorts are used, get them with:

```bash
kubectl -n rx-demo get svc rx-ui api-gateway
```

### 10. Confirm BKC API Trigger Token

The trigger endpoint requires an API key with:

```text
read:automation,write:automation
```

Keep the token in the shell environment, not in the repo:

```bash
export BKC_URL='http://<bkc-host>:5000'
export BKC_API_KEY='<redacted>'
```

Dry-run a harmless planned run:

```bash
curl -fsS -X POST "$BKC_URL/api/v1/automation/trigger" \
  -H "Authorization: Bearer $BKC_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "repo": "rx-demo",
    "workflow": "rx-demo-redeploy-from-git-event",
    "ref": "refs/heads/demo",
    "commit": "preflight",
    "notes": "preflight trigger only"
  }' | jq .
```

Expected:

- HTTP 202.
- A `run_id` is returned.
- `queued` may be `false` if no executor is wired for this workflow yet.

For the video, that is still useful as the event record. If the full executor is
not wired, run the shell deploy lane immediately after the trigger and describe
it as the current execution bridge.

## BKC UI Talk Track

Use this while walking through the four main BKC areas. Keep numbers framed as
rough lab estimates, not universal claims.

### 1. Overview

What to show:

- Resource counts.
- Recent and active pipeline runs.
- Failed or blocked runs.
- Jump points into graph, inventory, integrations, and pipelines.

Talk track:

> This is the morning operator view. Instead of asking "which terminal was I in"
> or "where did that command output go," BKC starts with current resources,
> recent automation, and the next operational branch point.

Man-hour framing:

- Manual mode: 15-30 minutes just to re-orient after a few days away from the
  lab.
- BKC mode: 2-5 minutes to find the current run, target, and validation state.
- Value: less context recovery and fewer repeated discovery commands.

Velocity note:

> The win is not that every task is fully automated on day one. The win is that
> every useful repair can become a better documented, more repeatable next run.

### 2. Resource Graph / Inventory

What to show:

- k3s nodes and cluster resources.
- rx-demo service resources.
- Registry, Docker, SSH, and observability relationships if present.
- The planned `Demo: K3s Add Node` pipeline.

Talk track:

> Most tools show one slice: Kubernetes objects, VMs, hosts, dashboards, or Git.
> The resource graph is where BKC tries to keep those slices connected enough
> for operations.

Man-hour framing:

- Manual mode: 30-90 minutes to reconstruct host-to-cluster-to-service
  relationships from notes, DNS, `kubectl`, SSH, and dashboards.
- BKC mode: 5-15 minutes to inspect the target resource and related actions.
- Value: faster handoff from "what is this thing?" to "what can I safely do?"

Velocity note:

> This is where we stop treating infrastructure as a pile of endpoints and start
> treating it as an operational map.

### 3. Integrations

What to show:

- SSH key readiness.
- Docker/registry or Swarm integration.
- Kubernetes/k3s scan target if available.
- Proxmox/Ansible as examples of adjacent execution tiers.

Talk track:

> BKC is intentionally not one executor. Some jobs belong to SSH, some to
> Ansible, some to Kubernetes, some to Docker or a registry, and some to Proxmox.
> The product idea is to put those execution paths behind one operator flow.

Man-hour framing:

- Manual mode: 1-3 hours to wire or re-check credentials, host access, registry
  paths, and cluster commands for a new workflow.
- BKC mode: 15-45 minutes once integrations and keys are in place.
- Value: fewer credential hunts, fewer one-off shell paths, clearer failure
  boundaries.

Velocity note:

> The integration work compounds. Once the SSH, registry, and k3s paths are
> proven, every later pipeline gets to reuse that foundation.

### 4. Pipelines

What to show:

- Tag filter: `demo`, `k3s`, `rx-demo`, `registry`, `add-node`.
- `Demo: K3s Add Node`.
- `Demo: Rx Demo K3s Registry Preflight`.
- `Demo: Rx Demo K3s Deploy`.
- `Demo: Rx Demo K3s Redeploy From Git`.
- `Demo: Rx Demo K3s Undeploy`.

Talk track:

> This is the central product moment. A pipeline is not just a shell script. It
> is a run record with stages, gates, targets, validation, and enough context to
> improve the next run.

Man-hour framing:

- Manual first pass: 4-8 hours to add a node, sort image distribution, deploy
  the app, debug routing, validate telemetry, and write down what happened.
- BKC-assisted repeat pass: 30-90 minutes if the environment is already close
  and the stages are represented.
- Mature pipeline target: 10-20 minutes of operator supervision for the same
  story, with failures captured in the run instead of scattered across shells.

Velocity note:

> The velocity story is cumulative: first we capture the work, then we make it
> repeatable, then we automate the safest parts, then we use run history to
> improve the next pipeline.

## Future Ticket / Issue Integration

This is a strong follow-up idea, but keep it out of the first live path unless
it is already tested.

Useful shape:

- GitHub issue opened or labeled `bkc-demo`.
- BKC imports the issue as an operational work item.
- A pipeline run links back to the issue.
- Pipeline completion posts a summary comment with commit, image tag, rollout,
  smoke result, and dashboard links.

Demo-safe wording:

> Today the trigger is a BKC API event from Git. The natural next step is issue
> and ticket integration: the request, deployment, validation, and completion
> note all stay connected.

Token handling:

- Do not create or paste a GitHub token during recording.
- Use a fine-grained token with only the required repository/issue scopes.
- Store it in BKC secret storage or the local shell, never in repo files.
- Consider a disposable demo repo or test organization for the first integration
  recording.

## Recording Script

Use this as the actual take outline.

### Scene 1: Product Setup In BKC

Show BKC overview and pipelines.

Say:

> We are using BKC as an operator control plane. The point is not only to deploy
> a Kubernetes app. The point is to keep the surrounding operational steps
> visible: host readiness, cluster state, deployment, telemetry validation, and
> run history.

Show:

- Resource graph or inventory.
- Pipeline list.
- Existing k3s telemetry or rx-demo lane.
- Folder-backed pipeline definitions in the editor, so the audience sees the
  same stages in source and the Web UI.

### Scene 2: k3s Readiness

Run or show the BKC/k3s readiness lane.

Operator sidestep before the add-node run:

- Refresh the Fedora 44 Proxmox base/template with the current BKC SSH public
  key.
- Keep VMID `131` as the visible clone source for the demo.
- Say that this is normal image hygiene: the pipeline should spend time adding
  capacity, not recovering from stale access baked into a base image.

Terminal fallback:

```bash
ssh <k3s-control-ssh> 'k3s kubectl get nodes -o wide'
ssh <k3s-control-ssh> 'k3s kubectl wait --for=condition=Ready nodes --all --timeout=90s'
```

Show in BKC:

- Run detail or pipeline stage.
- Stage named like `verify-k3s` or `node-readiness`.

First live operation order:

1. `Demo: Swarm Image Registry`.
2. Refresh or verify the Fedora 44 base/template SSH key for VMID `131`.
3. `Demo: Rx Demo K3s Registry Preflight`.
4. `Demo: K3s Add Node`.

### Scene 3: Initial Deploy

Run the deploy lane.

Terminal bridge:

```bash
cd /home/auzieman/Projects/rx-demo
DEMO_TAG="$(git rev-parse --short HEAD)"
PUSH=1 REGISTRY="$DEMO_REGISTRY" TAG="$DEMO_TAG" tools/build-and-push.sh
kubectl apply -k k8s/overlays/lab
kubectl -n rx-demo set image deploy/api-gateway api-gateway="$DEMO_REGISTRY/api-gateway:$DEMO_TAG"
kubectl -n rx-demo set image deploy/rx-ui rx-ui="$DEMO_REGISTRY/rx-ui:$DEMO_TAG"
kubectl -n rx-demo set image deploy/legacy-sync-worker worker="$DEMO_REGISTRY/legacy-sync-worker:$DEMO_TAG"
kubectl -n rx-demo set image deploy/read-model-projection worker="$DEMO_REGISTRY/read-model-projection:$DEMO_TAG"
kubectl -n rx-demo rollout status deploy/api-gateway --timeout=180s
kubectl -n rx-demo rollout status deploy/rx-ui --timeout=180s
kubectl -n rx-demo rollout status deploy/legacy-sync-worker --timeout=180s
kubectl -n rx-demo rollout status deploy/read-model-projection --timeout=180s
```

Show:

- BKC run history.
- `kubectl -n rx-demo get pods`.
- rx-ui in browser.

### Scene 4: Observability Proof

Switch to Grafana.

Show:

- `Rx Overview`: traffic and health.
- `Rx Service Flow`: API, worker, projection path.
- `Rx Executive Health`: component health.
- Optional Tempo trace for one synthetic request.

Say:

> This is the validation part of the pipeline. A deployment is not done because
> Kubernetes accepted YAML. It is done when the app is healthy and observable.

### Scene 5: Make The Live Change

Switch to editor.

Open:

```text
src/rx-ui/Rx.Ui/Pages/Index.cshtml
```

Change:

```html
<h1>Prescription Demo UI</h1>
```

to:

```html
<h1>Prescription Demo UI - BKC Redeploy</h1>
```

Commit:

```bash
cd /home/auzieman/Projects/rx-demo
git diff -- src/rx-ui/Rx.Ui/Pages/Index.cshtml
git add src/rx-ui/Rx.Ui/Pages/Index.cshtml
git commit -m "Demo BKC-triggered rx-ui redeploy"
```

If you do not want to leave the commit after rehearsal:

```bash
git reset --soft HEAD~1
git restore --staged src/rx-ui/Rx.Ui/Pages/Index.cshtml
```

Do not run cleanup until after recording.

### Scene 6: Trigger BKC From The Commit

Run:

```bash
cd /home/auzieman/Projects/rx-demo
COMMIT="$(git rev-parse --short HEAD)"

curl -fsS -X POST "$BKC_URL/api/v1/automation/trigger" \
  -H "Authorization: Bearer $BKC_API_KEY" \
  -H "Content-Type: application/json" \
  -d "{
    \"repo\": \"rx-demo\",
    \"workflow\": \"rx-demo-redeploy-from-git-event\",
    \"ref\": \"$(git rev-parse --abbrev-ref HEAD)\",
    \"commit\": \"$COMMIT\",
    \"notes\": \"Demo source change triggered rx-demo redeploy\"
  }" | jq .
```

Show:

- BKC API response.
- New BKC run in `/pipelines`.
- Commit hash in run details.

### Scene 7: Redeploy

If the BKC executor is fully wired, let BKC run it.

If using the shell bridge for the first video, run:

```bash
cd /home/auzieman/Projects/rx-demo
DEMO_TAG="$(git rev-parse --short HEAD)"
PUSH=1 REGISTRY="$DEMO_REGISTRY" TAG="$DEMO_TAG" tools/build-and-push.sh
kubectl -n rx-demo set image deploy/rx-ui rx-ui="$DEMO_REGISTRY/rx-ui:$DEMO_TAG"
kubectl -n rx-demo rollout status deploy/rx-ui --timeout=180s
```

If all images are rebuilt and applied:

```bash
kubectl apply -k k8s/overlays/lab
kubectl -n rx-demo rollout status deploy/rx-ui --timeout=180s
```

Show:

- BKC run remains the audit anchor.
- Terminal output is just the current execution bridge.
- The UI now shows `Prescription Demo UI - BKC Redeploy`.

### Scene 8: Final Validation

Show:

```bash
kubectl -n rx-demo get pods
kubectl -n rx-demo get svc rx-ui api-gateway
```

In Grafana:

- Request metrics continue.
- Logs/traces still arrive.
- Health panels remain acceptable after redeploy.

Close with:

> The commit changed the app, BKC captured the operational event, the deployment
> was rerun, and validation stayed attached to the run. That is the product
> concept.

## Optional MSSQL Instrumentation Segment

Do not make this the core recording unless it is already green.

Possible follow-up lanes:

- Telegraf `inputs.sqlserver` as a companion pod or sidecar.
- OpenTelemetry Collector `sqlserverreceiver`.
- Grafana Alloy `prometheus.exporter.mssql`.
- `sql_exporter` scraped by Prometheus.

For the first public video, mention this as the next instrumentation expansion:

> The app already emits service-level health and traces. The next layer is
> database-level telemetry from SQL Server into Prometheus/Grafana.

## Risk Register

- **Image visibility:** k3s may not see workstation-built images. Pre-flight the
  exact image distribution path.
- **Secrets:** avoid showing `.env`, Kubernetes secret values, or API keys.
- **Webhook semantics:** if no real webhook receiver is wired yet, call it an
  event-triggered pipeline using the BKC API.
- **ArgoCD comparison:** do not say BKC reconciles cluster state. Say BKC records
  and triggers broader operational workflows.
- **Long builds:** pre-build dependencies or use a warmed Docker cache before
  recording.
- **Grafana noise:** open the right dashboards before recording and verify time
  range is useful.

## Post-Rehearsal Cleanup

If the UI demo commit should not remain:

```bash
cd /home/auzieman/Projects/rx-demo
git reset --soft HEAD~1
git restore --staged src/rx-ui/Rx.Ui/Pages/Index.cshtml
git restore src/rx-ui/Rx.Ui/Pages/Index.cshtml
```

If temporary Kubernetes resources need cleanup:

```bash
kubectl -n rx-demo get pods
kubectl -n rx-demo rollout history deploy/rx-ui
```

Avoid deleting the namespace before checking whether dashboards or later
recording takes need the running workload.
