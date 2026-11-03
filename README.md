# 教會點名系統（attendance-system）

兒童主日學點名系統的雛型。資料模型以「事工 → 群組 → 人員」設計，同一套系統也可以用於青少年、小組等其他事工。

- 老師：手機開網頁（PWA，可加到主畫面）點名、新增新朋友
- 主責同工：成員管理、升班換組、統計報表、關懷名單、匯出 Excel
- 管理員：帳號、角色與負責群組

規劃見 [PLAN.md](PLAN.md)，資料表設計見 [docs/schema.md](docs/schema.md)。

## 專案結構

```
src/Attendance.Api   ASP.NET Core 10 Web API（EF Core、Identity、JWT）
  Auth/              角色、授權原則（角色與權限的對應集中在 Roles.cs）、JWT
  Domain/            資料模型
  Data/              DbContext（含稽核紀錄）、Migrations、初始資料
  Services/          點名邏輯、群組權限
  Endpoints/         API（auth / groups / people / users / reports）
tests/               自動化測試（xUnit，SQLite in-memory，不需資料庫伺服器）
web/                 React + TypeScript + Vite PWA
deploy/              docker-compose.yml、自動更新與 smoke test 腳本、部署說明
scripts/             工具腳本（SQL Server 連線測試）
.github/workflows/   CI/CD：測試 → 建置 image → 推上 Docker Hub
.githooks/           commit 前自動跑測試
Dockerfile           前端 + 後端打包成單一 image
```

## 本機開發

需要：.NET 10 SDK、Node.js 22.22 以上。

### 1. 設定機密（只存在本機 user-secrets，不進版控）

```powershell
cd src/Attendance.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=tcp:<server>,1433;Initial Catalog=<db>;User ID=<user>;Password=<password>;Encrypt=True;"
dotnet user-secrets set "Jwt:Key" "<至少 32 個隨機字元>"
dotnet user-secrets set "Seed:AdminUserName" "admin"
dotnet user-secrets set "Seed:AdminPassword" "<至少 8 碼，含英文小寫與數字>"
```

查看目前設定（含管理員密碼）：`dotnet user-secrets list`

### 2. 啟動後端（http://localhost:5080）

```powershell
cd src/Attendance.Api
dotnet run
```

啟動時會自動：套用 Migration → 建立角色 → 沒有任何帳號時建立第一個管理員 → Development 環境且資料庫沒有事工時，產生示範資料（2 個班、60 位孩子、8 週點名紀錄）。

### 3. 啟動前端（http://localhost:5173）

```powershell
cd web
npm install
npm run dev
```

開發時 `/api` 會轉給 `localhost:5080`。

### 測試

```powershell
dotnet test
```

**每次 commit 前都必須通過測試。** clone 後執行一次以下指令啟用 git hook，之後 commit 時會自動跑後端測試、
檢查 Migration 是否漏建、前端 lint 與型別檢查，約 15 秒，未通過就不會 commit：

```powershell
git config core.hooksPath .githooks
```

測試使用 SQLite in-memory 啟動整個 API，不需要 SQL Server 或 Docker。SQL Server 專屬的部分
（Migration、定序）由 CI 的 smoke test 用真正的 SQL Server 驗證；本機可在 WSL 執行 `sh deploy/smoke-test.sh`。

### 修改資料表

```powershell
dotnet tool restore   # 第一次使用時
cd src/Attendance.Api
# 修改 Domain/ 的類別或 Data/AppDbContext.cs 後：
dotnet ef migrations add <變更名稱> -o Data/Migrations
```

下次啟動時自動套用。

## 部署

推送到 `main` 後，GitHub Actions 會跑測試、建置 image、用 SQL Server 做 smoke test，再推上 Docker Hub。
遠端主機用 `deploy/docker-compose.yml` 執行，並以 cron 定期執行 `deploy/update.sh` 自動更新（更新前會先備份資料庫）。

設定步驟、版本回復、正式上線前的注意事項見 [deploy/README.md](deploy/README.md)。

### Azure

同一個 image 可部署到 Azure Container Apps，資料庫用 Azure SQL。以環境變數提供
`ConnectionStrings__Default`、`Jwt__Key`、`Seed__AdminUserName`、`Seed__AdminPassword`，
並設定 `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`。

## API 一覽

| 方法 | 路徑 | 權限 |
|---|---|---|
| GET | `/healthz`、`/api/version` | 匿名（健康檢查、目前版本） |
| POST | `/api/auth/login` | 匿名（每 IP 每分鐘 10 次） |
| GET | `/api/auth/me` | 登入 |
| POST | `/api/auth/change-password` | 登入 |
| GET | `/api/groups` | 登入（只列出有權限的群組） |
| GET / PUT | `/api/groups/{id}/rollcall?date=` | 該群組的老師 / 主責 / 管理員 |
| POST | `/api/groups/{id}/newcomers?date=` | 同上 |
| GET | `/api/ministries` | 主責、管理員 |
| GET / PUT | `/api/people`、`/api/people/{id}` | 主責、管理員 |
| POST | `/api/people/{id}/move` | 主責、管理員 |
| DELETE | `/api/people/{id}/memberships/{mid}` | 主責、管理員 |
| GET | `/api/reports/summary`、`absentees`、`newcomers`、`export` | 主責、管理員 |
| GET / POST / PUT | `/api/users`、`/api/users/{id}`、`/api/users/{id}/reset-password` | 管理員 |
