# 教會點名系統（attendance-system）

兒童主日學點名系統的雛型。資料模型以「事工 → 群組 → 人員」設計，同一套系統也可以用於青少年、小組等其他事工。

- 老師：手機開網頁（PWA，可加到主畫面）點名、新增新朋友
- 主責同工：成員管理、升班換組、統計報表、關懷名單、匯出 Excel
- 管理員：帳號、角色與負責群組

規劃見 [PLAN.md](PLAN.md)，資料表設計見 [docs/schema.md](docs/schema.md)。

## 專案結構

```
src/Attendance.Api   ASP.NET Core 10 Web API（EF Core、Identity、JWT）
  Domain/            資料模型
  Data/              DbContext（含稽核紀錄）、Migrations、初始資料
  Services/          點名邏輯、群組權限
  Endpoints/         API（auth / groups / people / users / reports）
web/                 React + TypeScript + Vite PWA
deploy/              docker-compose.yml、.env.example
scripts/             工具腳本（SQL Server 連線測試）
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

### 修改資料表

```powershell
cd src/Attendance.Api
# 修改 Domain/ 的類別或 Data/AppDbContext.cs 後：
dotnet ef migrations add <變更名稱> -o Data/Migrations
```

下次啟動時自動套用。

## 部署

### Docker（其他教會自架）

```bash
cd deploy
cp .env.example .env    # 填入 DB_SA_PASSWORD、JWT_KEY、ADMIN_PASSWORD
docker compose up -d --build
```

開啟 `http://<主機>:8080`，用 `.env` 的管理員帳號登入後**立即修改密碼**。

正式上線前：

- 在前面放反向代理（Caddy / Nginx）提供 HTTPS，並設定 `BEHIND_PROXY=true`
- 改用權限最小的資料庫帳號，不要讓 app 使用 `sa`
- 定期備份 `mssql-data` volume

### Azure

同一個 image 可部署到 Azure Container Apps，資料庫用 Azure SQL。以環境變數提供
`ConnectionStrings__Default`、`Jwt__Key`、`Seed__AdminUserName`、`Seed__AdminPassword`，
並設定 `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`。

## API 一覽

| 方法 | 路徑 | 權限 |
|---|---|---|
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
