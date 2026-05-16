# syntax=docker/dockerfile:1.7
FROM python:3.11-slim

WORKDIR /app
ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

# Dependencies
RUN pip install --no-cache-dir \
    requests \
    opentelemetry-api \
    opentelemetry-sdk \
    opentelemetry-exporter-otlp \
    opentelemetry-exporter-otlp-proto-http \
    opentelemetry-instrumentation-requests

# Load generator script
COPY src/loadgen/loadgen_http.py /app/loadgen_http.py

CMD ["python", "/app/loadgen_http.py"]
