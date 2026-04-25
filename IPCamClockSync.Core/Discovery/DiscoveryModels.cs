using System.Net;

namespace IPCamClockSync.Core.Discovery;

public sealed class DiscoveryOptions
{
    public int ProbeTimeoutSeconds { get; set; } = 5;

    public string MulticastAddress { get; set; } = "239.255.255.250";

    public int Port { get; set; } = 3702;

    /// <summary>
    /// 指定要綁定的網卡 IP 清單。null 或空清單表示自動偵測所有可用網卡。
    /// </summary>
    public IReadOnlyList<IPAddress>? BindAddresses { get; set; }

    public void Normalize()
    {
        ProbeTimeoutSeconds = ProbeTimeoutSeconds <= 0 ? 5 : ProbeTimeoutSeconds;
        MulticastAddress = string.IsNullOrWhiteSpace(MulticastAddress) ? "239.255.255.250" : MulticastAddress.Trim();
        Port = Port is <= 0 or > 65535 ? 3702 : Port;
    }
}

public sealed class DiscoveredCamera
{
    public string DeviceId { get; init; } = string.Empty;

    /// <summary>從 XAddrs 解析出的 IPv4 位址（如有），否則為 EndpointReference Address。</summary>
    public string Address { get; init; } = string.Empty;

    public string ServiceAddress { get; init; } = string.Empty;

    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    public string Model { get; init; } = string.Empty;
}
