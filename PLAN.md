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
/web                     React PWA
/deploy                  docker-compose.yml、.env.example、部署說明
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
- API 使用 JWT（短效 access token + refresh token），全程 HTTPS
- 所有新增 / 修改 / 刪除寫入稽核紀錄

## 資料設計重點

詳見 [docs/schema.md](docs/schema.md)。

- 用「事工 → 群組 → 成員」的通用模型，不寫死為「班級 / 學生」
- 家長也是 `People` 的一筆，透過關係表連結，未來可同時是成人小組成員
- 點名時整份名單的狀態都寫入（含缺席），報表才算得出出席率
- 群組成員資格有起訖日期，升班 / 換組後歷史統計不會跑掉
- 新朋友欄位尚在討論：核心欄位先做，其餘用 EF Core Migration 增加，或放進 `CustomFields`（JSON）

## 個資保護

- 兒童為未成年人，新朋友登記需記錄家長同意（時間、方式）
- 敏感欄位（健康 / 過敏）僅限有需要的角色可見
- 應用程式使用最小權限資料庫帳號，不使用管理者帳號
- 連線字串、JWT 金鑰等以環境變數 / user-secrets 提供，不進版控

## 部署

- **本教會**：Azure Container Apps（用量計費、可縮到 0）+ Azure SQL（Basic 或免費方案）
- **其他教會**：`deploy/docker-compose.yml` 一鍵啟動 app + SQL Server 容器
- 啟動時自動套用 EF Core Migrations；首次啟動以環境變數建立第一個 Admin 帳號

## 階段

| 階段 | 內容 | 狀態 |
|---|---|---|
| 0 | 規劃、資料表設計、專案骨架 | 完成 |
| 1 MVP | 登入與角色、成員管理、點名、新增新朋友 | 雛型完成；事工 / 群組的新增編輯畫面尚未做 |
| 2 報表 | 出席統計、關懷名單、新朋友留存、Excel 匯出 | 雛型完成；趨勢圖尚未做 |
| 3 完善 | Excel 批次匯入、升班作業、稽核查詢、Docker 部署流程 | 個人換組 + Dockerfile / compose 已有；批次升班、匯入、稽核查詢畫面未做 |

## 雛型尚未涵蓋

- 事工 / 群組的新增與編輯畫面（目前由示範資料建立）
- 新朋友重複登記的提醒（同名同生日）
- refresh token：目前 access token 12 小時到期需重新登入
- 主責同工的成員查詢尚未依事工限制範圍（點名與報表已限制）
- 自動化測試
- 離線點名

## 待討論

- [ ] 新朋友登記欄位
- [ ] 登入方式是否加入 Google / LINE
- [ ] 現有名單匯入格式
- [ ] 固定報表格式（例如月報）
