# 教會點名系統 — 開發規劃

## 目標

- 老師用手機（PWA）快速點名、新增新朋友
- 後台網頁管理成員、群組、帳號，查看紀錄
- 統計報表（出席率、趨勢、關懷名單、新朋友留存），可匯出 Excel
- 帳號密碼登入 + 角色權限

## 範圍與規模

- 起始：兒童主日學 2 堂，每堂 1 班，每班約 50 人
- 設計目標：同一間教會的多個事工（兒童、青少年、小組…），數千人
- **單一教會一套部署**（不做多租戶）。其他教會要用時，以 Docker 在其機房架設獨立一套

## 技術選型

| 部分 | 技術 |
|---|---|
| 後端 | ASP.NET Core (.NET 10 LTS) Web API、EF Core、ASP.NET Core Identity |
| 前端 | React + TypeScript + Vite + vite-plugin-pwa（點名頁與後台同一專案，依角色顯示） |
| 資料庫 | SQL Server（Azure SQL 或 Docker 版 SQL Server） |
| 報表匯出 | ClosedXML（Excel） |
| 部署 | 單一 Docker image（API 同時提供前端靜態檔） |

## 專案結構

```
/src/Attendance.Api      ASP.NET Core Web API（含 EF Core Migrations）
/tests/Attendance.Api.Tests  自動化測試
/web                     React PWA
/deploy                  docker-compose.yml、.env.example、自動更新腳本、部署說明
/.github/workflows       CI/CD
/docs                    資料表設計等文件
Dockerfile               多階段建置：web build → dotnet publish → runtime image
```

## 角色與權限

| 角色 | 權限 |
|---|---|
| Admin 系統管理員 | 全部，含帳號管理、系統設定 |
| Coordinator 主責同工 | 所負責事工下的所有群組、成員、報表 |
| Leader 老師 / 小組長 | 僅自己負責的群組：點名、新增新朋友、查看名單 |

- 密碼強度要求、登入失敗鎖定、管理者可重設密碼、停用帳號立即生效
- API 使用 JWT，全程 HTTPS。access token 12 小時到期；**不做 refresh token**（每週只有一個聚會時段，到期重新登入即可）
- 端點依「能做什麼」使用授權原則（ManagePeople、ViewReports…），角色與權限的對應集中在一處，之後調整權限只改那裡
- 主責同工目前可管理所有成員；之後要限制在所負責事工時，只需修改 `GroupAccess.People()`
- 所有新增 / 修改 / 刪除寫入稽核紀錄

## 資料設計重點

詳見 [docs/schema.md](docs/schema.md)。

- 用「事工 → 群組 → 成員」的通用模型，不寫死為「班級 / 學生」
- 家長也是 `People` 的一筆，透過關係表連結，未來可同時是成人小組成員
- 點名時整份名單的狀態都寫入（含缺席），報表才算得出出席率
- 群組成員資格有起訖日期，升班 / 換組後歷史統計不會跑掉
- 新朋友登記：姓名、生日、年級、電話（只有姓名必填，加上家長同意）。其他需要的欄位之後用 Migration 增加，或放進 `CustomFields`（JSON）
- 年級以「入學年度」儲存，每年 8 月新學年自動升一級，不必每年修改
- 已預留之後可能用到的欄位（學校、地址、LINE ID、健康備註、外部編號、群組適用年級、帳號對應人員等），
  在正式資料進來前先建好，減少之後的 Migration；詳見 [docs/schema.md](docs/schema.md)

## 個資保護

- 兒童為未成年人，新朋友登記需記錄家長同意（時間、方式）
- 敏感欄位（健康 / 過敏）僅限有需要的角色可見
- 應用程式使用最小權限資料庫帳號，不使用管理者帳號
- 連線字串、JWT 金鑰等以環境變數 / user-secrets 提供，不進版控

## 部署

- **CI/CD**：推送到 main → GitHub Actions 跑測試 → 建置 image → 用 SQL Server 做 smoke test → 推上 Docker Hub
- **遠端主機**：`deploy/docker-compose.yml` 啟動 app + SQL Server；cron 定期執行 `deploy/update.sh`，
  有新版時先備份資料庫再更新（詳見 [deploy/README.md](deploy/README.md)）
- **其他教會**：同上，用同一個 Docker Hub image 架設獨立一套
- 啟動時自動套用 EF Core Migrations；首次啟動以環境變數建立第一個 Admin 帳號
- **測試**：每次 commit 前由 git hook 自動跑測試（`git config core.hooksPath .githooks`），CI 也會再跑一次

## 階段

| 階段 | 內容 | 狀態 |
|---|---|---|
| 0 | 規劃、資料表設計、專案骨架 | 完成 |
| 1 MVP | 登入與角色、成員管理、點名、新增新朋友 | 雛型完成（含事工 / 群組管理、直接新增成員） |
| 2 報表 | 出席統計、關懷名單、新朋友留存、Excel 匯出 | 雛型完成；趨勢圖尚未做 |
| 3 完善 | Excel 批次匯入、升班作業、稽核查詢、Docker 部署流程 | 個人換組、Excel 匯入、CI/CD、自動更新已完成；批次升班、稽核查詢畫面未做 |

## 尚未完成

- 停課標記：`Sessions.IsCancelled` 報表已會排除，但還沒有設定停課的 API / 畫面
- 家長 / 監護人的編輯（目前只在新增新朋友時寫入；新朋友表單已簡化，只留聯絡電話、不填家長姓名）
- 新朋友重複登記的提醒（同名同生日）
- 趨勢圖
- 批次升班（可搭配已預留的群組適用年級 `MinGrade` / `MaxGrade`）
- 稽核紀錄查詢畫面（資料已有寫入）
- 敏感欄位（健康 / 過敏）的畫面與權限控管（欄位與授權原則 `ViewSensitiveInfo` 已預留）
- Google / LINE 登入
- 離線點名
- 最小權限的資料庫帳號、HTTPS 反向代理（見 deploy/README.md）

## 已決定

- 登入方式要加入 Google / LINE（帳號密碼登入保留；外部登入使用 Identity 內建的 `AspNetUserLogins`）
- 報表：目前能匯出 Excel 即可，固定報表格式（例如月報）之後依需求調整
- 名單匯入：成員管理 →「匯入 Excel」，下載範本填寫後上傳，先預覽再匯入；已存在的成員略過不覆蓋（範本欄位之後可依需求調整）
- 新朋友登記欄位：姓名、生日、年級、電話（只有姓名必填）
- 不做 refresh token
- 權限：目前不需要更細的限制，但架構上保留調整空間（見「角色與權限」）
