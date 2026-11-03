# 部署說明

```
git push（main）──► GitHub Actions ──► 測試 ──► 建置 image ──► SQL Server smoke test ──► Docker Hub
                                                                                         │
                                       遠端主機 cron 定期執行 update.sh ◄────────────────┘
                                       （有新版 → 備份資料庫 → 更新 → 確認啟動）
```

## 一、設定 GitHub → Docker Hub（只需一次）

1. 在 Docker Hub 建立 repository `nkec-attendance-system`（公開或私人皆可）
2. Docker Hub → Account settings → Personal access tokens，建立一個 **Read & Write** 的 token
3. GitHub repo → Settings → Secrets and variables → Actions：
   - **Variables**（或 Secrets）新增 `DOCKERHUB_USERNAME` = Docker Hub 帳號
   - **Secrets** 新增 `DOCKERHUB_TOKEN` = 上一步的 token

之後每次推送到 `main`，通過測試的 image 會以這些標籤推上 Docker Hub：

| 標籤 | 何時產生 | 用途 |
|---|---|---|
| `latest` | 推送到 main | 一般使用，自動更新會追這個 |
| `sha-abc1234` | 每次推送 | 固定在某次 commit，用於回到舊版 |
| `1.2.0`、`1.2` | 打標籤 `git tag v1.2.0 && git push --tags` | 正式版本 |

## 二、遠端主機首次部署

主機需要 Docker（含 compose plugin）與 curl。主機上不需要原始碼，只要 `deploy/` 底下的三個檔案：

```bash
sudo mkdir -p /opt/attendance && cd /opt/attendance
# 從 repo 複製 docker-compose.yml、update.sh、.env.example 到這裡
cp .env.example .env
nano .env        # 填入 APP_IMAGE、DB_SA_PASSWORD、JWT_KEY、ADMIN_PASSWORD
docker login     # Docker Hub repository 是私人的才需要
docker compose up -d
```

開啟 `http://<主機>:8080`，用 `.env` 的管理員帳號登入後**立即修改密碼**。
啟動時會自動建立資料表（套用 Migration）。

## 三、自動更新

`update.sh` 會檢查 Docker Hub 有沒有新版。沒有就結束；有的話會：

1. 備份資料庫到 `mssql-data` volume 的 `/var/opt/mssql/backup/attendance-<時間>.bak`（保留 30 天）
2. 換成新版 app（新版若有 Migration 會在啟動時自動套用）
3. 等待 `/healthz` 回應正常；失敗時印出錯誤紀錄並保留備份

用 cron 定期執行（`crontab -e`，執行的帳號需在 `docker` 群組）。建議選在沒有聚會的時段，例如台灣時間每天凌晨 3 點。
cron 用的是**主機的時區**，先用 `timedatectl` 確認；多數雲端主機是 UTC，台灣凌晨 3 點＝UTC 19:00：

```cron
# 主機時區為 Asia/Taipei
0 3 * * * sh /opt/attendance/update.sh >> /opt/attendance/update.log 2>&1
# 主機時區為 UTC
0 19 * * * sh /opt/attendance/update.sh >> /opt/attendance/update.log 2>&1
```

想立即更新就手動執行 `sh /opt/attendance/update.sh`。

> 沒有使用 Watchtower：原版 Watchtower 已停止維護，且不相容 Docker 29 以後的版本。

## 四、版本與回復

- 目前執行的版本：`curl http://localhost:8080/api/version`
- 回到舊版：把 `.env` 的 `APP_IMAGE` 改成 `...:sha-<舊 commit>` 後執行 `docker compose up -d`。
  之後 update.sh 只會追這個固定標籤，等問題修好再改回 `:latest`。
- 新版已經變更資料表時，舊版程式不一定能用新的資料表，需要先還原備份：

```bash
docker compose stop app
docker compose exec db sh -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "RESTORE DATABASE [attendance] FROM DISK = N'"'"'/var/opt/mssql/backup/attendance-<時間>.bak'"'"' WITH REPLACE"'
docker compose up -d app
```

備份檔放在同一台主機上，請另外定期複製到其他地方（例如 `docker compose cp db:/var/opt/mssql/backup ./backup`）。

## 五、正式上線前

- 在前面放反向代理（Caddy / Nginx）提供 HTTPS，並在 `.env` 設定 `BEHIND_PROXY=true`
- 改用權限最小的資料庫帳號，不要讓 app 使用 `sa`

## 其他

- **不用 Docker Hub，從原始碼建置**：`docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build`
- **本機驗證 image 與 SQL Server Migration**（WSL 內執行）：`sh deploy/smoke-test.sh`
