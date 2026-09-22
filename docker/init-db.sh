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

# 每次部署都执行 init.sql（首次建库；此后作为幂等结构同步）。
# init.sql 是全部数据库对象与初始数据的唯一维护入口，建库/建表/补列/种子均按存在性判断，
# 老库由此自动对齐结构（新增表、新增列、新增种子数据），避免"代码已升级、库结构没跟上"
# 导致的运行时异常（如 Invalid column name 'Env'）。
DB_COUNT=$(sqlcmd -S "$DB_HOST" -U sa -P "$SA_PASSWORD" -C -h -1 -W -Q "SELECT COUNT(*) FROM sys.databases WHERE name = 'ConvenientSystem'")

if [ "$DB_COUNT" = "0" ]; then
    echo "Database does not exist. Running init.sql (first-time initialization)..."
else
    echo "Database exists. Running init.sql (idempotent schema sync)..."
fi

# 不加 -b（单条语句报错不中断后续，老库历史差异可能带来无害报错）；
# 同步失败只告警、不阻塞容器启动，错误细节保留在部署日志中供排查。
set +e
sqlcmd -S "$DB_HOST" -U sa -P "$SA_PASSWORD" -C -d master -i /init.sql -f 65001
SQL_EXIT=$?
set -e

if [ "$SQL_EXIT" != "0" ]; then
    echo "WARNING: init.sql finished with errors (exit code $SQL_EXIT). See output above; schema may be partially synced."
else
    echo "Database schema synced successfully."
fi

echo "=== Database initialization complete ==="
