# IPCamClockSync

IPCamClockSync 是一套以 **.NET 8** 開發的工具，用於在區網中 **掃描 ONVIF 攝影機**，並提供 **批次時間同步 / 批次 NTP 設定** 的能力。  
專案同時提供 **互動式 Console GUI** 與 **完整 CLI**；另包含可獨立部署的 **NTP Server**（可延伸為 Windows Service）。

> 目前專案正從 MVP 推進至 V1：Phase 0/1 已完成，Phase 2/4/5 進行中（詳見 `plan/mainPlan.md`）。

---

## Features

- ONVIF 掃描（WS-Discovery）
  - 廣播探測、收斂去重、逾時控制
  - 支援 **網卡綁定**（多網卡環境更穩定）
- 設定與清單持久化
  - `settings.yaml`（設定載入/儲存、預設值、遺漏欄位回填）
  - `cameras.json`（攝影機清單、schema/欄位驗證、含 NTP IP 欄位）
- 帳密處理
  - 預設採 **Base64 混淆**（非密碼學加密）避免明碼落盤
- Console GUI + CLI 雙入口
  - 無參數：走 Console GUI
  - 有參數：走 CLI
- NTP Server（獨立可執行）
  - 目前已有最小可用回應服務
  - 後續可擴充成 Windows Service + 防火牆規則生命週期管理

---

## Repository Structure

- `IPCamClockSync.Core`：核心通訊、掃描、設定/清單、共用邏輯
- `IPCamClockSync.ConsoleGui`：互動式 Console GUI
- `IPCamClockSync.Cli`：命令列工具
- `IPCamClockSync.NtpServer`：獨立 NTP server（UDP 123）
- `IPCamClockSync.Tests`：單元測試
- `plan/`：計畫與里程碑（建議先看 `plan/mainPlan.md`）
- `doc/`：參考資料與筆記

---

## Quick Start

### Prerequisites

- .NET 8 SDK
- Windows 為主要目標環境（涉及防火牆/Service 時更完整）；掃描功能在一般桌面環境亦可使用
- 需在與攝影機相同網段或具備可達路由

（更詳細環境請見：`環境需求.md`）

### Build & Run

```bash
dotnet build
```

- 互動式 Console GUI（無參數）

```bash
dotnet run --project IPCamClockSync
```

- CLI 模式（帶參數）

```bash
dotnet run --project IPCamClockSync -- /h
```

---

## CLI (Current)

> 指令與輸出仍在完善中；實際可用命令以 `/h` 顯示為準。

- `/h`：Help（含指令分群）
- `/scan`：掃描 ONVIF 設備並輸出清單（`cameras.json`）
- `/a`：手動單次時間更新（Manual）
- `/usentp <ntp-ip>`：設定 NTP 伺服器並切換時間來源到 NTP
- `/set-ntp <ntp-ip>`：相容別名，等同 `/usentp <ntp-ip>`
- 防火牆：`status|enable|disable|repair`（已可用）
- 服務管理：`install|uninstall|status`（已可用）
- NTP Server：`/ntpserver start|stop|restart|status`（依實作進度）

---

## Project Status / Roadmap

計畫與進度請見：`plan/mainPlan.md`

- Phase 0：✅ 完成（規格定版、solution/分層、設定/清單格式、防火牆模式）
- Phase 1：✅ 完成（掃描/相容性、持久化、設定管理、Base64 帳密）
- Phase 2：✅ 完成（手動時間更新與 NTP 模式切換分流、實機驗證完成）
- Phase 3：🟡 部分完成（最小 NTP Server 可運作；Service/安裝/防火牆生命週期待補）
- Phase 4：🚧 進行中（GUI 流程與 UX 持續完善）
- Phase 5：🚧 進行中（CLI 指令樹、靜默輸出規格與錯誤碼表待完善）
- Phase 6：⬜ 待開始（日誌輪替、部署、穩定性/壓力測試、correlationId 全流程追蹤）

---

## Testing

- 單元測試：設定載入/回填、清單序列化、命令解析、Base64 帳密處理

```bash
dotnet test
```

（測試說明請見：`測試.md`）

---

## Notes / Security

- 帳密 Base64 僅為「混淆」避免明碼，不等同加密。若需更高安全性，後續可改用 DPAPI / Windows Credential Manager 等。

---

## License

MIT License — see [LICENSE](LICENSE).
