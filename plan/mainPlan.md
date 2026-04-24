## Main Plan: IPCamClockSync MVP to V1

以 .NET 8 為基礎，維持 Console GUI + 完整 CLI 的雙入口體驗。
前端入口採單一執行檔 IPCamClockSync（無參數開 GUI、有參數走 CLI），NTP Server 維持獨立可執行檔/服務。
帳密儲存策略已調整為 Base64 混淆方案（非明碼），並保留設定開關；日誌採 JSONL + 輪替。

## Steps
1. Phase 0 - 規格定版與專案骨架（已完成）
   1.1 定義命令列介面與互動式 Console GUI 最小流程（掃描、保存、單次更新、設定 NTP、設定頁）。
   1.2 建立 solution 與分層：Core、Console GUI、CLI、NtpServer、Tests。
   1.3 定義設定與清單格式：settings.yaml、cameras.json、日誌輪替規則。
   1.4 定義防火牆 Open/Strict 預設檔，供 GUI/CLI 一鍵切換。

2. Phase 1 - 核心通訊與資料層（已完成，含相容性修復）
   2.1 ONVIF 掃描服務：WS-Discovery 廣播、收斂、去重、逾時。
      - 已補強多 probe 相容策略，並加入網卡綁定選項。
      - CLI /scan 已可在實網掃到設備並落盤。
   2.2 Camera 清單持久化：JSON 讀寫、Schema 版本欄位、欄位驗證。
      - 已含 ntpServerIp 欄位與基礎驗證。
   2.3 設定管理：YAML 載入/儲存、預設值、遺漏欄位回填。
   2.4 帳密儲存：改為 Base64 混淆（非明碼），含回退策略與錯誤處理。

3. Phase 2 - 時間同步能力（進行中）
   3.1 單次時間更新：每台更新前重讀系統時間，推送本機時區與時間。
      - 現況：/a 已有更新前檢查流程（含重試與錯誤分類），尚待接上完整 ONVIF 時間推送。
   3.2 NTP 設定推送：對清單攝影機批次套用目標 NTP IP。
      - 現況：/set-ntp 已可批次寫入清單；尚待實際推送到設備。
   3.3 連線控制：逾時、重試、失敗分類（授權、網路、協議）。
      - 現況：已落地 auth/timeout/network/unknown 分類基礎。
   3.4 並行更新能力：單執行緒/多執行緒切換與最大併發數。
      - 現況：設定欄位已存在，執行引擎尚待完成。

4. Phase 3 - NTP Server 與服務控制（部分完成，可與 Phase 2 平行）
   4.1 實作獨立 NTP Server 執行檔（UDP 123、狀態日誌、監控欄位）。
      - 現況：最小 NTP 回應服務已可運作。
   4.2 時間來源策略：先系統時間，保留外部上游擴充點。
   4.3 Windows Service：LocalService、failure actions。
   4.4 安裝雙路徑：CLI install|uninstall，MSI 可勾選安裝。
   4.5 防火牆自動設定：UDP 123 入站規則（最小權限）。
   4.6 防火牆規則生命週期：status/repair/disable/保留或移除策略。
   4.7 CLI：/ntpserver start|stop|restart|status。
   4.8 service install 預設 delayed-auto + 描述 + failure actions。

5. Phase 4 - Console GUI 體驗優先落地（進行中）
   5.1 首次聲明與說明頁。
   5.2 掃描流程畫面：動畫、結果分頁、進階掃描。
      - 現況：已完成 BIOS 風格操作與多輪 UX 微調，並新增網卡綁定選單。
   5.3 清單保存流程：多選保存、逐台覆寫帳密、儲存路徑詢問。
   5.4 單次更新與 NTP 設定流程化。
   5.5 設定頁：掃描時長、逾時、併發、匯出。
   5.6 NTP 服務控制頁：狀態、防火牆、設定編輯。

6. Phase 5 - CLI 完整化（進行中）
   6.1 既有命令：/a、/ntpserver start|stop|restart。
   6.2 新增命令：/scan、/set-ntp、/validate、/export、/h（已可用）。
   6.3 靜默模式輸出規範：成功/失敗碼、摘要列、錯誤碼表（部分完成）。
   6.4 防火牆命令：status|enable|disable|repair（已可用）。
   6.5 服務管理命令：install|uninstall|status（已可用）。
   6.6 指令分群 help 樹狀輸出（已初步落地）。

7. Phase 6 - 日誌、部署與穩定化（待完成）
   7.1 檔案輪替日誌：大小/檔數/按日策略。
   7.2 安裝版與免安裝版：初始化與升級策略。
   7.3 可靠性測試：大量設備、離線節點、帳密錯誤、NTP 壓力。
   7.4 JSONL channel + correlationId 全流程追蹤。

## Verification
1. 單元測試：設定載入/回填、清單序列化、命令解析、Base64 帳密處理。
2. CLI 實機驗證：/scan 可掃描並輸出 cameras.json；/set-ntp 與 /a 可執行。
3. GUI 驗證：主流程可操作，網卡綁定選單可用。
4. 服務驗證：/ntpserver service 與 firewall 子命令可操作。

## Decisions (Updated)
- .NET 版本維持 .NET 8 LTS。
- 前端入口採單一執行檔 IPCamClockSync（GUI/CLI 分流）。
- NTP Server 維持獨立執行檔與 Windows Service。
- 帳密儲存採 Base64 混淆策略（非明碼；非密碼學加密）。
- 防火牆策略維持 Open 與 Strict 兩種模式，可由 CLI/GUI 切換。
- 日誌落地維持 JSONL + channel + correlationId + 輪替。

## Session Change Log
- feat: 完成 Phase 1 並改為 Base64 密碼儲存
- feat: 推進 Phase 2 並補齊 update/set-ntp 流程
- 修復 WS-Discovery 掃描相容性並新增網卡綁定選單

## Current Status
- Phase 0: 完成
- Phase 1: 完成（含 WS-Discovery 相容性修復）
- Phase 2: 進行中（已完成基礎連線控制與命令接線）
- Phase 3: 部分完成（最小 NTP server + service/firewall 控制已具備）
- Phase 4: 進行中（GUI 基礎功能與 UX 已有可用版本）
- Phase 5: 進行中（核心命令可用，待完善錯誤碼與輸出規範）
- Phase 6: 待開始

## Next Targets
1. 完成真正 ONVIF 時間推送（對應 Phase 2.1）。
2. 完成真正 NTP 設定下發到設備（對應 Phase 2.2）。
3. 補齊併發更新引擎與回報彙整（對應 Phase 2.4）。
4. 補齊 CLI 靜默輸出規格與錯誤碼表（對應 Phase 5.3）。
