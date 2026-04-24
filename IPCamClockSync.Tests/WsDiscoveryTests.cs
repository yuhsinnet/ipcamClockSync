using IPCamClockSync.Core.Discovery;

namespace IPCamClockSync.Tests;

public sealed class WsDiscoveryTests
{
    [Fact]
    public void BuildProbeMessage_ShouldContainNetworkVideoTransmitterType()
    {
        var xml = WsDiscoveryMessageBuilder.BuildProbeMessage(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        Assert.Contains("NetworkVideoTransmitter", xml, StringComparison.Ordinal);
        Assert.Contains("urn:uuid:11111111-1111-1111-1111-111111111111", xml, StringComparison.Ordinal);
      Assert.Contains("http://www.w3.org/2005/08/addressing", xml, StringComparison.Ordinal);
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