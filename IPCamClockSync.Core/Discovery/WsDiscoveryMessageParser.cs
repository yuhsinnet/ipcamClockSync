using System.Xml.Linq;

namespace IPCamClockSync.Core.Discovery;

public static class WsDiscoveryMessageParser
{
    public static IReadOnlyList<DiscoveredCamera> ParseProbeMatches(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var document = XDocument.Parse(xml);
        XNamespace soap = "http://www.w3.org/2003/05/soap-envelope";
        XNamespace discovery = "http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01";
        XNamespace addressing = "http://www.w3.org/2005/08/addressing";

        var matches = document
            .Descendants(discovery + "ProbeMatch")
            .Select(match =>
            {
                var address = match.Element(addressing + "EndpointReference")?.Element(addressing + "Address")?.Value?.Trim() ?? string.Empty;
                var serviceAddress = match.Element(discovery + "XAddrs")?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                var scopes = match.Element(discovery + "Scopes")?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

                return new DiscoveredCamera
                {
                    DeviceId = address,
                    Address = address,
                    ServiceAddress = serviceAddress,
                    Scopes = scopes,
                };
            })
            .Where(camera => !string.IsNullOrWhiteSpace(camera.ServiceAddress) || !string.IsNullOrWhiteSpace(camera.Address))
            .GroupBy(camera => string.IsNullOrWhiteSpace(camera.ServiceAddress) ? camera.Address : camera.ServiceAddress, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return matches;
    }
}
