#!/bin/sh
# 用 docker-compose.yml 搭配真正的 SQL Server 啟動整套系統，確認 migration 能套用、能登入、前端有出來。
# 自動化測試用的是 SQLite，這裡補上 SQL Server 的驗證。CI 每次建置 image 後都會跑。
#
#   deploy/smoke-test.sh           從原始碼建置 image 後測試（本機可在 WSL 執行）
#   deploy/smoke-test.sh <image>   測試指定的 image
set -eu
cd "$(dirname "$0")"

image=${1:-}
if [ -z "$image" ]; then
  image=attendance-system:smoke
  docker build -t "$image" ..
fi

# Shell variables take precedence over a local deploy/.env, so real settings are never used here.
export APP_IMAGE="$image"
export DB_SA_PASSWORD="Smoke-$(date +%s)-Aa1"
export JWT_KEY="smoke-test-only-signing-key-0123456789"
export ADMIN_USERNAME=admin
export ADMIN_PASSWORD=smoke1234
export APP_PORT="${SMOKE_PORT:-18080}"
url="http://localhost:$APP_PORT"

compose() { docker compose -p attendance-smoke -f docker-compose.yml "$@"; }
cleanup() { compose down -v --remove-orphans >/dev/null 2>&1 || true; }
trap cleanup EXIT

compose up -d

echo "等待 app 啟動（SQL Server 首次啟動約需 30–60 秒）"
healthy=
for _ in $(seq 1 60); do
  if curl -fsS "$url/healthz" >/dev/null 2>&1; then healthy=1; break; fi
  sleep 3
done
if [ -z "$healthy" ]; then
  echo "app 沒有在時間內啟動"
  compose logs --tail 100
  exit 1
fi

curl -fsS -X POST "$url/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"userName\":\"$ADMIN_USERNAME\",\"password\":\"$ADMIN_PASSWORD\"}" >/dev/null
curl -fsS "$url/" | grep -q 'id="root"'

echo "smoke test 通過：$(curl -fsS "$url/api/version")"
