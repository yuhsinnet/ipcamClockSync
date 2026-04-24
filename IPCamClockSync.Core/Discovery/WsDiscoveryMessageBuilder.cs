namespace IPCamClockSync.Core.Discovery;

public static class WsDiscoveryMessageBuilder
{
    public static string BuildProbeMessage(Guid messageId)
    {
        return $"""
<?xml version=\"1.0\" encoding=\"utf-8\"?>
<e:Envelope xmlns:e=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:w=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" xmlns:d=\"http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01\" xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\">
  <e:Header>
    <w:MessageID>urn:uuid:{messageId}</w:MessageID>
    <w:To>urn:docs-oasis-open-org:ws-dd:ns:discovery:2009:01</w:To>
    <w:Action>http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01/Probe</w:Action>
  </e:Header>
  <e:Body>
    <d:Probe>
      <d:Types>dn:NetworkVideoTransmitter</d:Types>
    </d:Probe>
  </e:Body>
</e:Envelope>
""";
    }
}
