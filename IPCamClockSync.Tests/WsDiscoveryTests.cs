using IPCamClockSync.Core.Discovery;

namespace IPCamClockSync.Tests;

public sealed class WsDiscoveryTests
{
    [Fact]
    public void BuildProbeSequence_ShouldContainNetworkVideoTransmitterType()
    {
        var multicast = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("239.255.255.250"), 3702);
        var broadcast = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("255.255.255.255"), 3702);

        var sequence = WsDiscoveryMessageBuilder.BuildProbeSequence(multicast, broadcast).ToList();
        var firstPayload = System.Text.Encoding.UTF8.GetString(sequence[0].Payload);

        Assert.Equal(8, sequence.Count);
        Assert.Contains("NetworkVideoTransmitter", firstPayload, StringComparison.Ordinal);
        Assert.Contains("http://www.w3.org/2005/08/addressing", firstPayload, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseProbeMatches_ShouldDeduplicateByServiceAddress()
    {
        var xml = """
<e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope" xmlns:d="http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01" xmlns:a="http://www.w3.org/2005/08/addressing">
  <e:Body>
    <d:ProbeMatches>
      <d:ProbeMatch>
        <a:EndpointReference><a:Address>urn:uuid:cam-1</a:Address></a:EndpointReference>
        <d:Scopes>onvif://www.onvif.org/type/video_encoder</d:Scopes>
        <d:XAddrs>http://192.168.1.10/onvif/device_service</d:XAddrs>
      </d:ProbeMatch>
      <d:ProbeMatch>
        <a:EndpointReference><a:Address>urn:uuid:cam-2</a:Address></a:EndpointReference>
        <d:Scopes>onvif://www.onvif.org/type/video_encoder</d:Scopes>
        <d:XAddrs>http://192.168.1.10/onvif/device_service</d:XAddrs>
      </d:ProbeMatch>
    </d:ProbeMatches>
  </e:Body>
</e:Envelope>
""";

        var matches = WsDiscoveryMessageParser.ParseProbeMatches(xml);

        Assert.Single(matches);
        Assert.Equal("http://192.168.1.10/onvif/device_service", matches[0].ServiceAddress);
    }
}