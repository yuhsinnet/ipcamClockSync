using System.Net;

namespace IPCamClockSync.Core.Discovery;

public static class WsDiscoveryMessageBuilder
{
    private const int BroadcastProbeRepeatCount = 5;
    private static readonly TimeSpan ProbeRetryInterval = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// 返回完整 probe 發包序列：(payload, endpoint) 三種訊息，廣播重複 5 次。
    /// </summary>
    public static IEnumerable<(byte[] Payload, IPEndPoint Endpoint, TimeSpan Delay)> BuildProbeSequence(
        IPEndPoint multicastEndpoint,
        IPEndPoint broadcastEndpoint)
    {
        // 1. OASIS 2009 NVT probe (新版，部分廠商支援)
        yield return (Encode(BuildOasisNvtProbeMessage($"urn:uuid:{Guid.NewGuid()}")), multicastEndpoint, ProbeRetryInterval);

        // 2. 舊版 WS-Discovery tds:Device probe (Hikvision、Dahua 等主流廠商)
        yield return (Encode(BuildLegacyDeviceProbeMessage($"uuid:{Guid.NewGuid()}")), multicastEndpoint, ProbeRetryInterval);

        // 3. 舊版 WS-Discovery NetworkVideoTransmitter probe
        yield return (Encode(BuildLegacyNvtProbeMessage($"uuid:{Guid.NewGuid()}")), multicastEndpoint, ProbeRetryInterval);

        // 4. Uniview 廣播 probe，重複 5 次
        var broadcastPayload = Encode(BuildUniviewBroadcastProbeMessage($"urn:uuid:{Guid.NewGuid()}"));
        for (int i = 0; i < BroadcastProbeRepeatCount; i++)
        {
            yield return (broadcastPayload, broadcastEndpoint, ProbeRetryInterval);
        }
    }

    private static byte[] Encode(string message) =>
        System.Text.Encoding.UTF8.GetBytes(message);

    // OASIS WS-DD 2009 NVT probe
    private static string BuildOasisNvtProbeMessage(string messageId) =>
        $"""<?xml version="1.0" encoding="utf-8"?>""" +
        $"""<e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope" xmlns:w="http://www.w3.org/2005/08/addressing" xmlns:d="http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01" xmlns:dn="http://www.onvif.org/ver10/network/wsdl">""" +
        $"""<e:Header><w:MessageID>{messageId}</w:MessageID><w:To>urn:docs-oasis-open-org:ws-dd:ns:discovery:2009:01</w:To><w:Action>http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01/Probe</w:Action></e:Header>""" +
        $"""<e:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></e:Body>""" +
        $"""</e:Envelope>""";

    // 舊版 WS-Discovery 2005 tds:Device probe (Hikvision、Dahua 主流)
    private static string BuildLegacyDeviceProbeMessage(string messageId) =>
        """<?xml version="1.0" encoding="UTF-8"?>""" +
        """<Envelope xmlns:tds="http://www.onvif.org/ver10/device/wsdl" xmlns="http://www.w3.org/2003/05/soap-envelope">""" +
        """<Header>""" +
        $"""<wsa:MessageID xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing">{messageId}</wsa:MessageID>""" +
        """<wsa:To xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing">urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To>""" +
        """<wsa:Action xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</wsa:Action>""" +
        """</Header>""" +
        """<Body>""" +
        """<Probe xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns="http://schemas.xmlsoap.org/ws/2005/04/discovery">""" +
        """<Types>tds:Device</Types><Scopes/>""" +
        """</Probe></Body></Envelope>""";

    // 舊版 WS-Discovery 2005 NetworkVideoTransmitter probe
    private static string BuildLegacyNvtProbeMessage(string messageId) =>
        """<?xml version="1.0" encoding="UTF-8"?>""" +
        """<e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope" xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery" xmlns:dn="http://www.onvif.org/ver10/network/wsdl">""" +
        $"""<e:Header><w:MessageID>{messageId}</w:MessageID>""" +
        """<w:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>""" +
        """<w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action></e:Header>""" +
        """<e:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></e:Body>""" +
        """</e:Envelope>""";

    // Uniview 廣播相容 probe
    private static string BuildUniviewBroadcastProbeMessage(string messageId) =>
        """<?xml version="1.0" encoding="UTF-8"?>""" +
        """<SOAP-ENV:Envelope xmlns:SOAP-ENV="http://www.w3.org/2003/05/soap-envelope" xmlns:SOAP-ENC="http://www.w3.org/2003/05/soap-encoding" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:tns="http://schemas.xmlsoap.org/ws/2005/04/discovery" xmlns:dn="http://www.onvif.org/ver10/network/wsdl">""" +
        $"""<SOAP-ENV:Header><wsa:MessageID>{messageId}</wsa:MessageID>""" +
        """<wsa:To SOAP-ENV:mustUnderstand="true">urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To>""" +
        """<wsa:Action SOAP-ENV:mustUnderstand="true">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</wsa:Action></SOAP-ENV:Header>""" +
        """<SOAP-ENV:Body><tns:UniviewProbe><tns:Types>dn:NetworkVideoTransmitter</tns:Types></tns:UniviewProbe></SOAP-ENV:Body>""" +
        """</SOAP-ENV:Envelope>""";
}
