#!/bin/sh
# 檢查 Docker Hub 上有沒有新版 image；有的話先備份資料庫，再換成新版並確認啟動成功。
# 沒有新版時什麼都不做，所以可以放進 cron 定期執行（設定方式見 deploy/README.md）。
#
#   ./update.sh            檢查並更新
#   KEEP_BACKUP_DAYS=60 ./update.sh   備份保留天數（預設 30 天）
set -eu
cd "$(dirname "$0")"

log() { echo "$(date '+%F %T') $*"; }

port=$(sed -n 's/^APP_PORT=//p' .env 2>/dev/null | tr -d '\r')
url="http://localhost:${port:-8080}"

wait_healthy() {
  for _ in $(seq 1 40); do
    if curl -fsS "$url/healthz" >/dev/null 2>&1; then return 0; fi
    sleep 3
  done
  return 1
}

container=$(docker compose ps -q app)
if [ -z "$container" ]; then
  log "app 尚未啟動，直接啟動"
  docker compose up -d
  wait_healthy && log "已啟動：$(curl -fsS "$url/api/version")"
  exit
fi

running=$(docker inspect --format '{{.Image}}' "$container")
# The image configured for app in .env (config --images would also list db).
image=$(docker compose config app | awk '/^  app:/ { inapp = 1; next } /^  [^ ]/ { inapp = 0 } inapp && /^    image:/ { print $2; exit }')
docker compose pull --quiet app
latest=$(docker image inspect --format '{{.Id}}' "$image")

if [ "$running" = "$latest" ]; then
  log "已是最新版（$image）"
  exit 0
fi

# New versions may migrate the database on startup, so take a backup first.
stamp=$(date +%Y%m%d-%H%M%S)
log "發現新版 $image，備份資料庫 attendance-$stamp.bak"
docker compose exec -T -e STAMP="$stamp" -e KEEP_DAYS="${KEEP_BACKUP_DAYS:-30}" db sh -s <<'EOF'
set -e
mkdir -p /var/opt/mssql/backup
/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b \
  -Q "BACKUP DATABASE [attendance] TO DISK = N'/var/opt/mssql/backup/attendance-$STAMP.bak' WITH INIT"
find /var/opt/mssql/backup -name 'attendance-*.bak' -mtime +"$KEEP_DAYS" -delete
EOF

log "更新 app"
docker compose up -d app

if wait_healthy; then
  log "更新完成：$(curl -fsS "$url/api/version")"
  docker image prune -f >/dev/null
else
  log "更新後 app 沒有正常啟動，請檢查以下紀錄；資料庫備份為 attendance-$stamp.bak"
  docker compose logs --tail 80 app
  exit 1
fi
