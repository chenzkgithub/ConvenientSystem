#!/bin/bash
set -e

export PATH=$PATH:/opt/mssql-tools18/bin:/opt/mssql-tools/bin

echo "=== ConvenientSystem Database Initialization ==="
echo "Waiting for SQL Server to be ready..."

# Wait for SQL Server (max 60 attempts = 120 seconds)
for i in $(seq 1 60); do
    if sqlcmd -S "$DB_HOST" -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" > /dev/null 2>&1; then
        echo "SQL Server is ready (attempt $i)."
        break
    fi
    if [ $i -eq 60 ]; then
        echo "ERROR: SQL Server not ready after 120 seconds."
        exit 1
    fi
    sleep 2
done

# Check if database already exists (idempotent)
DB_COUNT=$(sqlcmd -S "$DB_HOST" -U sa -P "$SA_PASSWORD" -C -h -1 -W -Q "SELECT COUNT(*) FROM sys.databases WHERE name = 'ConvenientSystem'")

if [ "$DB_COUNT" = "0" ]; then
    echo "Database does not exist. Running init.sql..."
    sqlcmd -S "$DB_HOST" -U sa -P "$SA_PASSWORD" -C -d master -i /init.sql -f 65001
    echo "Database initialized successfully."
else
    echo "Database already exists. Skipping initialization."
fi

echo "=== Database initialization complete ==="
