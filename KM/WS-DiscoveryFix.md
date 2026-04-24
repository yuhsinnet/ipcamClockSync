# WS-Discovery 維修經驗總結

## 背景

本次問題表現為 WS-Discovery 掃描結果不穩定或掃不到設備，但實際網路中存在可回應 ONVIF 的攝影機。修復後以 CLI 實測可掃描出 14 台設備。

## 問題根因

### 1. Probe 相容性不足

原本只送出單一新版 OASIS 2009 的 NetworkVideoTransmitter probe。

實際上不同廠牌攝影機常使用不同版本的 WS-Discovery / WS-Addressing：

- Hikvision、Dahua 等常見設備偏向舊版 WS-Discovery 2005 / WS-Addressing 2004。
- 部分設備需要 `tds:Device` probe 才會回應。
- Uniview 類設備可能對廣播 probe 的相容性較好。

### 2. Response Parser 命名空間支援不足

原本 parser 只解析 OASIS 2009 namespace，造成即使設備有回應，也可能因為 namespace 不符而被完全忽略。

### 3. Socket 未加入 multicast group

若 socket 沒有正確 `JoinMulticastGroup`，即使送出 probe，也可能收不到 multicast 回應。

### 4. 缺少 fallback 機制

當回應內容中的 `XAddrs` 或 `EndpointReference` 無法完整解析時，原本流程缺少用遠端 endpoint IP 補救的能力，導致設備被漏掉。

## 修復內容

### 1. 擴充 Probe 發送策略

在 `WsDiscoveryMessageBuilder` 中改為建立完整 probe sequence：

- OASIS 2009 `NetworkVideoTransmitter`
- Legacy WS-Discovery `tds:Device`
- Legacy WS-Discovery `NetworkVideoTransmitter`
- Uniview broadcast probe（重送 5 次）

每個 probe 之間加入短暫延遲，提升不同設備的相容性與回應率。

### 2. Parser 同時支援新版與舊版命名空間

在 `WsDiscoveryMessageParser` 中同時支援：

- `http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01`
- `http://schemas.xmlsoap.org/ws/2005/04/discovery`
- `http://www.w3.org/2005/08/addressing`
- `http://schemas.xmlsoap.org/ws/2004/08/addressing`

並優先從 `XAddrs` 中抽取可用的 IPv4 位址。

### 3. 增加 remote endpoint fallback

若 XML 解析不到可用位址，則改用 UDP 回應的 `RemoteEndPoint.Address` 建立最小可用的攝影機記錄，避免漏掃。

### 4. 改善 Socket 設定

在 `OnvifWsDiscoveryService` 中補上：

- `ReuseAddress`
- `Broadcast`
- `ReceiveBuffer` / `SendBuffer`
- `MulticastInterface`
- `MulticastTimeToLive = 1`
- `JoinMulticastGroup`

這些設定可明顯改善多網卡、區網 multicast、不同設備回應穩定性。

### 5. 加入 GUI 網卡綁定選單

在 Console GUI 掃描前新增網卡選單，預設為「全部網卡（自動偵測）」，可改選單一卡進行綁定掃描。

## 驗證結果

### Build

全專案 `dotnet build -c Debug` 成功。

### 掃描實測

使用以下命令驗證：

```powershell
dotnet run --project IPCamClockSync -- /scan
```

實測結果：

- 掃描完成
- 找到 14 台攝影機
- 結果已寫入 `config/cameras.json`

## 這次維修的關鍵結論

### 1. ONVIF / WS-Discovery 不能只押單一標準版本

理論標準一致，不代表設備實作一致。實務上需要同時兼容新版與舊版命名空間、不同 probe 類型與部分廠商特化行為。

### 2. Discovery parser 要以「可恢復」為優先

不要假設所有設備都會提供完整合法的 `XAddrs`。只要能從回應或遠端端點推回可用 IP，就應盡量保留掃描結果。

### 3. 多網卡環境是常態，不是例外

在 Windows 上，開發機常同時有實體網卡、虛擬網卡、VPN、Hyper-V、Docker 等介面。Discovery 若沒有處理多網卡與綁定需求，很容易出現掃描不準或完全掃不到的情況。

### 4. GUI 與 CLI 都需要可控制的網卡綁定行為

後續功能應讓 GUI 與 CLI 共用同一份掃描網卡設定，並支援：

- 預設全部網卡
- 記住使用者上次所選網卡
- 用 MAC 作為網卡識別鍵
- 比對目前環境與已記錄網卡資訊（名稱 / IP / MAC）
- 任一變動即視為環境變更，回復預設值

## 後續建議

### 建議新增的設定欄位

可在 `settings.yaml` 的 `scan` 區段增加：

- `preferredNicMac`
- `lastKnownNicSnapshot`
- `preferredNicMode`（例如 `all` / `single`）

### 建議後續實作

1. CLI 新增 `/scan /nic <mac|all>` 支援
2. GUI 預設選中上次記錄的網卡
3. 啟動掃描前自動比對目前網卡環境是否變更
4. 若環境變更，重置為「全部網卡」
5. 補單元測試：舊版 namespace、fallback、NIC snapshot 比對、CLI `/nic`

## 相關檔案

- `IPCamClockSync.Core/Discovery/WsDiscoveryMessageBuilder.cs`
- `IPCamClockSync.Core/Discovery/WsDiscoveryMessageParser.cs`
- `IPCamClockSync.Core/Discovery/OnvifWsDiscoveryService.cs`
- `IPCamClockSync.Core/Discovery/DiscoveryModels.cs`
- `IPCamClockSync.ConsoleGui/ConsoleGuiApp.cs`

## 備註

本次修復已經證明 Discovery 核心流程可用；下一步重點不再是「能否掃到」，而是「如何穩定記住並管理使用者的網卡選擇，以及在環境變更時安全回退到預設行為」。