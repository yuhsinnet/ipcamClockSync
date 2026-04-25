using System.Net;
using System.Net.Sockets;
using System.Xml.Linq;

namespace IPCamClockSync.Core.Discovery;

public static class WsDiscoveryMessageParser
{
    // OASIS WS-DD 2009（新版）
    private static readonly XNamespace DiscoveryOasis = "http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01";
    private static readonly XNamespace AddressingW3C = "http://www.w3.org/2005/08/addressing";

    // WS-Discovery 2005（舊版，Hikvision / Dahua / Uniview 等主流廠商）
    private static readonly XNamespace DiscoveryLegacy = "http://schemas.xmlsoap.org/ws/2005/04/discovery";
    private static readonly XNamespace AddressingLegacy = "http://schemas.xmlsoap.org/ws/2004/08/addressing";

    /// <summary>
    /// 解析 WS-Discovery ProbeMatch 回應，同時支援 OASIS 2009 與舊版 2005 命名空間。
    /// <paramref name="remoteAddress"/> 作為備援：當 XML 無法解析出有效 IP 時，改用遠端位址。
    /// </summary>
    public static IReadOnlyList<DiscoveredCamera> ParseProbeMatches(string xml, IPAddress? remoteAddress = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<DiscoveredCamera>();
        }

        try
        {
            var document = XDocument.Parse(xml);
            var parsed = ParseWithNamespace(document, DiscoveryOasis, AddressingW3C)
                .Concat(ParseWithNamespace(document, DiscoveryLegacy, AddressingLegacy))
                .ToList();

            // 若解析出結果，按 IP / ServiceAddress 去重後返回
            if (parsed.Count > 0)
            {
                return parsed
                    .GroupBy(c => string.IsNullOrWhiteSpace(c.IpAddress) ? c.ServiceAddress : c.IpAddress, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(c => c.HasOnvifXAddress ? 1 : 0).First())
                    .Select(c => new DiscoveredCamera
                    {
                        DeviceId = c.Address,
                        Address = c.IpAddress,
                        ServiceAddress = c.ServiceAddress,
                        Scopes = c.Scopes,
                        Model = ExtractModelFromScopes(c.Scopes),
                    })
                    .Where(c => !string.IsNullOrWhiteSpace(c.Address) || !string.IsNullOrWhiteSpace(c.ServiceAddress))
                    .ToArray();
            }
        }
        catch
        {
            // XML 解析失敗，走備援
        }

        // 備援：XML 無法使用時，若有 remoteAddress 則以 IP 建立最小記錄
        if (remoteAddress is not null && remoteAddress.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(remoteAddress))
        {
            return new[]
            {
                new DiscoveredCamera
                {
                    DeviceId = remoteAddress.ToString(),
                    Address = remoteAddress.ToString(),
                    ServiceAddress = string.Empty,
                    Scopes = Array.Empty<string>(),
                }
            };
        }

        return Array.Empty<DiscoveredCamera>();
    }

    private static IEnumerable<ParsedMatch> ParseWithNamespace(XDocument document, XNamespace discovery, XNamespace addressing)
    {
        foreach (var match in document.Descendants(discovery + "ProbeMatch"))
        {
            var address = match.Element(addressing + "EndpointReference")
                ?.Element(addressing + "Address")?.Value?.Trim() ?? string.Empty;

            var xaddrs = match.Element(discovery + "XAddrs")?.Value
                ?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

            var scopes = (match.Element(discovery + "Scopes")?.Value
                ?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>())
                .ToArray();

            // 從所有 XAddr 中找第一個可解析為 IPv4 的
            string serviceAddress = string.Empty;
            string ipAddress = string.Empty;
            bool hasOnvifXAddress = false;

            foreach (var token in xaddrs)
            {
                if (!Uri.TryCreate(token, UriKind.Absolute, out var uri)) continue;
                if (!IPAddress.TryParse(uri.Host, out var ip)) continue;
                if (ip.AddressFamily != AddressFamily.InterNetwork) continue;

                serviceAddress = token;
                ipAddress = ip.ToString();
                hasOnvifXAddress = token.Contains("/onvif", StringComparison.OrdinalIgnoreCase);
                break;
            }

            // 若 XAddrs 沒有找到 IP，嘗試從 EndpointReference Address 提取
            if (string.IsNullOrWhiteSpace(ipAddress) && !string.IsNullOrWhiteSpace(address))
            {
                if (Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
                    IPAddress.TryParse(uri.Host, out var ip) &&
                    ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    ipAddress = ip.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(ipAddress) && string.IsNullOrWhiteSpace(serviceAddress) && string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            yield return new ParsedMatch
            {
                Address = address,
                IpAddress = ipAddress,
                ServiceAddress = serviceAddress,
                HasOnvifXAddress = hasOnvifXAddress,
                Scopes = scopes,
            };
        }
    }

    private sealed class ParsedMatch
    {
        public string Address { get; init; } = string.Empty;
        public string IpAddress { get; init; } = string.Empty;
        public string ServiceAddress { get; init; } = string.Empty;
        public bool HasOnvifXAddress { get; init; }
        public string[] Scopes { get; init; } = Array.Empty<string>();
    }

    private static string ExtractModelFromScopes(IEnumerable<string> scopes)
    {
        foreach (var scope in scopes)
        {
            if (TryExtractModelScope(scope, "onvif://www.onvif.org/name/", out var model) ||
                TryExtractModelScope(scope, "onvif://www.onvif.org/hardware/", out model) ||
                TryExtractModelScope(scope, "onvif://www.onvif.org/model/", out model))
            {
                return model;
            }
        }

        return string.Empty;
    }

    private static bool TryExtractModelScope(string scope, string prefix, out string model)
    {
        model = string.Empty;
        if (!scope.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = scope[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        model = Uri.UnescapeDataString(value).Trim();
        return !string.IsNullOrWhiteSpace(model);
    }
}
