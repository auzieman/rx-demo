"""
Synthetic load generator for the rx-demo API.
Sends a configurable mix of GET/POST requests at a target RPS for a set duration.
Instrumented with OpenTelemetry so loadgen spans appear alongside app traces.
"""

import os
import random
import string
import time
import threading
import json
import requests
from concurrent.futures import ThreadPoolExecutor

from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.resources import Resource
from opentelemetry.instrumentation.requests import RequestsInstrumentor
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.exporter.otlp.proto.http.metric_exporter import OTLPMetricExporter
from opentelemetry.metrics import Observation, set_meter_provider, get_meter_provider

# ── Config from env ──────────────────────────────────────────────
BASE_URL = os.getenv("BASE_URL", "http://localhost:8080")
OTLP_ENDPOINT = os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4318")
SERVICE_NAME = os.getenv("SERVICE_NAME", "rx-loadgen")
SERVICE_NAMESPACE = os.getenv("SERVICE_NAMESPACE", "rx")
SERVICE_ENV = os.getenv("ENV", "dev")
HEALTH_WEIGHT = float(os.getenv("HEALTH_WEIGHT", "10"))
HEALTH_ERROR_PENALTY = float(os.getenv("HEALTH_ERROR_PENALTY", "15"))
RPS = int(os.getenv("RPS", "2"))
DURATION = int(os.getenv("DURATION_SECONDS", "0"))
WORKERS = int(os.getenv("WORKERS", "4"))
MIX_READ = float(os.getenv("MIX_READ", "0.20"))
MIX_APPROVE = float(os.getenv("MIX_APPROVE", "0.40"))
MIX_REFILL = float(os.getenv("MIX_REFILL", "0.40"))
FAULT_RATE = float(os.getenv("FAULT_RATE", "0.0"))
PROGRESS_INTERVAL_SECONDS = int(os.getenv("PROGRESS_INTERVAL_SECONDS", "30"))
FAULT_MODES = [
    item.strip()
    for item in os.getenv(
        "FAULT_MODES",
        "worker-transient-once,projection-fail,cache-fail,api-slow",
    ).split(",")
    if item.strip()
]

# ── OTel setup ───────────────────────────────────────────────────
resource = Resource.create({
    "service.name": SERVICE_NAME,
    "service.namespace": SERVICE_NAMESPACE,
    "deployment.environment": SERVICE_ENV,
})
provider = TracerProvider(resource=resource)
exporter = OTLPSpanExporter(endpoint=f"{OTLP_ENDPOINT}/v1/traces")
provider.add_span_processor(BatchSpanProcessor(exporter))
trace.set_tracer_provider(provider)
tracer = trace.get_tracer(__name__)
RequestsInstrumentor().instrument()
metric_reader = PeriodicExportingMetricReader(
    OTLPMetricExporter(endpoint=f"{OTLP_ENDPOINT}/v1/metrics")
)
set_meter_provider(MeterProvider(resource=resource, metric_readers=[metric_reader]))
meter = get_meter_provider().get_meter("rx.loadgen", "1.0.0")
request_counter = meter.create_counter("rx.synthetic.requests_total")
request_duration = meter.create_histogram("rx.synthetic.duration_ms", unit="ms")
request_errors = meter.create_counter("rx.synthetic.errors_total")
_component_errors = []


def component_health():
    cutoff = time.monotonic() - 300
    with _stats_lock:
        recent_errors = sum(1 for event_at in _component_errors if event_at >= cutoff)
    return max(0.0, 100.0 - (recent_errors * HEALTH_ERROR_PENALTY))


def health_band(health):
    return "green" if health >= 90 else "yellow" if health >= 60 else "red"


def observe_component_health(_options):
    health = component_health()
    band = "green" if health >= 90 else "yellow" if health >= 60 else "red"
    return [
        Observation(health, {"component": SERVICE_NAME, "health_band": band})
    ]


def observe_component_weight(_options):
    return [
        Observation(HEALTH_WEIGHT, {"component": SERVICE_NAME})
    ]


def observe_component_status(_options):
    health = component_health()
    status = 2 if health >= 90 else 1 if health >= 60 else 0
    return [
        Observation(status, {"component": SERVICE_NAME, "health_band": health_band(health)})
    ]


def observe_component_errors(_options):
    cutoff = time.monotonic() - 300
    with _stats_lock:
        recent_errors = sum(1 for event_at in _component_errors if event_at >= cutoff)
    return [
        Observation(recent_errors, {"component": SERVICE_NAME})
    ]


meter.create_observable_gauge(
    "rx.component.health_percent",
    callbacks=[observe_component_health],
    unit="%",
    description="Rolling component health score. Starts at 100 and subtracts health penalty for recent errors.",
)
meter.create_observable_gauge(
    "rx.component.weight_percent",
    callbacks=[observe_component_weight],
    unit="%",
    description="Executive weighting assigned to this component for overall health rollups.",
)
meter.create_observable_gauge(
    "rx.component.health_status",
    callbacks=[observe_component_status],
    description="Component health band: 2=green, 1=yellow, 0=red.",
)
meter.create_observable_gauge(
    "rx.component.error_events_5m",
    callbacks=[observe_component_errors],
    description="Error events recorded by this component in the last five minutes.",
)

# ── Helpers ──────────────────────────────────────────────────────
_counter = 0
_lock = threading.Lock()


def _rx_id():
    """Generate a sequential prescription ID."""
    global _counter
    with _lock:
        _counter += 1
        return f"RX-{_counter:06d}"


def _random_user():
    return "user_" + "".join(random.choices(string.ascii_lowercase, k=4))


# ── Request functions ────────────────────────────────────────────
session = requests.Session()
session.headers.update({"Content-Type": "application/json"})
_stats_lock = threading.Lock()
_stats = {"requests": 0, "errors": 0, "faults": 0}


def log_event(event_name, **fields):
    payload = {
        "event.domain": "rx",
        "event.name": event_name,
        "service.name": SERVICE_NAME,
        "deployment.environment": SERVICE_ENV,
        **fields,
    }
    print(json.dumps(payload, sort_keys=True), flush=True)


def record_result(operation, rx_id, started_at, status_code=None, outcome="ok", error_type=None):
    duration_ms = (time.perf_counter() - started_at) * 1000.0
    attrs = {
        "operation": operation,
        "outcome": outcome,
    }
    if status_code is not None:
        attrs["status_code"] = str(status_code)

    request_counter.add(1, attrs)
    request_duration.record(duration_ms, attrs)

    with _stats_lock:
        _stats["requests"] += 1

    if error_type:
        request_errors.add(1, {"operation": operation, "error_type": error_type})
        with _stats_lock:
            _stats["errors"] += 1
            _component_errors.append(time.monotonic())
            cutoff = time.monotonic() - 300
            while _component_errors and _component_errors[0] < cutoff:
                _component_errors.pop(0)

    log_event(
        f"loadgen.{operation}",
        **{
            "rx.id": rx_id,
            "http.status_code": status_code,
            "result": outcome,
            "duration.ms": round(duration_ms, 2),
            "error.type": error_type,
        }
    )


def maybe_fault_mode(operation):
    if FAULT_RATE <= 0 or not FAULT_MODES:
        return None

    if random.random() >= FAULT_RATE:
        return None

    if operation == "read":
        valid_modes = ["api-slow", "api-error"]
    elif operation in {"approve", "refill"}:
        valid_modes = [
            mode for mode in FAULT_MODES
            if mode in {
                "api-slow",
                "api-error",
                "worker-transient-once",
                "worker-timeout",
                "worker-fail",
                "publish-fail",
                "projection-fail",
                "projection-timeout",
                "cache-fail",
            }
        ]
    else:
        valid_modes = FAULT_MODES

    if not valid_modes:
        return None

    with _stats_lock:
        _stats["faults"] += 1

    return random.choice(valid_modes)


def do_read():
    rx = _rx_id()
    fault_mode = maybe_fault_mode("read")
    started_at = time.perf_counter()
    with tracer.start_as_current_span("loadgen.read"):
        try:
            params = {"faultMode": fault_mode} if fault_mode else None
            r = session.get(f"{BASE_URL}/prescriptions/{rx}", params=params, timeout=5)
            r.raise_for_status()
            record_result("read", rx, started_at, status_code=r.status_code)
        except Exception as exc:
            status_code = getattr(getattr(exc, "response", None), "status_code", None)
            record_result("read", rx, started_at, status_code=status_code, outcome="error", error_type=type(exc).__name__)


def do_approve():
    rx = _rx_id()
    fault_mode = maybe_fault_mode("approve")
    started_at = time.perf_counter()
    with tracer.start_as_current_span("loadgen.approve"):
        try:
            r = session.post(
                f"{BASE_URL}/prescriptions/{rx}/approve",
                json={"approvedBy": _random_user(), "notes": "loadgen", "faultMode": fault_mode},
                timeout=5,
            )
            r.raise_for_status()
            record_result("approve", rx, started_at, status_code=r.status_code)
        except Exception as exc:
            status_code = getattr(getattr(exc, "response", None), "status_code", None)
            record_result("approve", rx, started_at, status_code=status_code, outcome="error", error_type=type(exc).__name__)


def do_refill():
    rx = _rx_id()
    fault_mode = maybe_fault_mode("refill")
    started_at = time.perf_counter()
    with tracer.start_as_current_span("loadgen.refill"):
        try:
            r = session.post(
                f"{BASE_URL}/prescriptions/{rx}/refill",
                json={"refillCount": 1, "faultMode": fault_mode},
                timeout=5,
            )
            r.raise_for_status()
            record_result("refill", rx, started_at, status_code=r.status_code)
        except Exception as exc:
            status_code = getattr(getattr(exc, "response", None), "status_code", None)
            record_result("refill", rx, started_at, status_code=status_code, outcome="error", error_type=type(exc).__name__)


def pick_action():
    total_mix = MIX_READ + MIX_APPROVE + MIX_REFILL
    roll = random.random()
    read_cutoff = MIX_READ / total_mix
    approve_cutoff = read_cutoff + (MIX_APPROVE / total_mix)

    if roll < read_cutoff:
        return do_read
    elif roll < approve_cutoff:
        return do_approve
    else:
        return do_refill


# ── Main loop ────────────────────────────────────────────────────
def main():
    total_mix = MIX_READ + MIX_APPROVE + MIX_REFILL
    if total_mix <= 0:
        raise ValueError("Traffic mix must be greater than zero.")

    interval = 1.0 / max(RPS, 1)
    log_event(
        "loadgen.start",
        target=BASE_URL,
        rps=RPS,
        duration_seconds=DURATION,
        workers=WORKERS,
        mix_read=round(MIX_READ / total_mix, 2),
        mix_approve=round(MIX_APPROVE / total_mix, 2),
        mix_refill=round(MIX_REFILL / total_mix, 2),
        fault_rate=FAULT_RATE,
        fault_modes=FAULT_MODES,
    )

    deadline = None if DURATION <= 0 else time.monotonic() + DURATION
    sent = 0
    last_progress_at = time.monotonic()

    with ThreadPoolExecutor(max_workers=WORKERS) as pool:
        while deadline is None or time.monotonic() < deadline:
            action = pick_action()
            pool.submit(action)
            sent += 1
            now = time.monotonic()
            if now - last_progress_at >= max(PROGRESS_INTERVAL_SECONDS, 5):
                elapsed = 0 if deadline is None else DURATION - (deadline - now)
                with _stats_lock:
                    stats = dict(_stats)
                log_event(
                    "loadgen.progress",
                    sent=sent,
                    elapsed_seconds=round(elapsed, 0),
                    requests=stats["requests"],
                    errors=stats["errors"],
                    faults=stats["faults"],
                )
                last_progress_at = now
            time.sleep(interval)

    with _stats_lock:
        stats = dict(_stats)
    log_event("loadgen.done", sent=sent, requests=stats["requests"], errors=stats["errors"], faults=stats["faults"])
    get_meter_provider().shutdown()
    provider.shutdown()


if __name__ == "__main__":
    main()
