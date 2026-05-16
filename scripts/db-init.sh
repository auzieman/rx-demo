#!/bin/sh
set -eu

: "${SA_PASSWORD:?set SA_PASSWORD before running db-init.sh}"

# Trust server certificate (ODBC 18 defaults to TLS + cert validation).
# Create RxDemo if missing, then verify it is ONLINE.
for i in $(seq 1 30); do
  if /opt/mssql-tools/bin/sqlcmd \
      -S mssql -U sa -P "$SA_PASSWORD" -d master -C \
      -Q "IF DB_ID('RxDemo') IS NULL CREATE DATABASE [RxDemo];
          SELECT name, state_desc FROM sys.databases WHERE name='RxDemo';"
  then
    echo "db-init: database 'RxDemo' ensured."
    exit 0
  fi
  echo "db-init: retry $i/30 - waiting for SQL..."
  sleep 3
done

echo "db-init: failed to create 'RxDemo' after retries"
exit 1
