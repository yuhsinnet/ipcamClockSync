using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using IPCamClockSync.Core.Data;

namespace IPCamClockSync.Core.Services;

public sealed class OnvifDeviceManagementService : IOnvifDeviceManagementService
{
    public async Task<OnvifOperationResult> SetSystemDateAndTimeAsync(
        CameraRecord camera,
        DateTimeOffset localNow,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var timezoneId = BuildOnvifTimezone(localNow.Offset);
        var utcNow = localNow.ToUniversalTime();
        var body =
            $"<tds:SetSystemDateAndTime xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\">" +
            "<tds:DateTimeType>Manual</tds:DateTimeType>" +
            "<tds:DaylightSavings>false</tds:DaylightSavings>" +
            $"<tds:TimeZone><tt:TZ>{timezoneId}</tt:TZ></tds:TimeZone>" +
            "<tds:UTCDateTime>" +
            $"<tt:Time><tt:Hour>{utcNow.Hour}</tt:Hour><tt:Minute>{utcNow.Minute}</tt:Minute><tt:Second>{utcNow.Second}</tt:Second></tt:Time>" +
            $"<tt:Date><tt:Year>{utcNow.Year}</tt:Year><tt:Month>{utcNow.Month}</tt:Month><tt:Day>{utcNow.Day}</tt:Day></tt:Date>" +
            "</tds:UTCDateTime>" +
            "</tds:SetSystemDateAndTime>";

        return await SendDeviceRequestAsync(camera, body, timeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OnvifOperationResult> SetNtpServerAsync(
        CameraRecord camera,
        string ntpIp,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var body =
            "<tds:SetNTP xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\">" +
            "<tds:FromDHCP>false</tds:FromDHCP>" +
            "<tds:NTPManual>" +
            "<tt:Type>IPv4</tt:Type>" +
            $"<tt:IPv4Address>{WebUtility.HtmlEncode(ntpIp)}</tt:IPv4Address>" +
            "</tds:NTPManual>" +
            "</tds:SetNTP>";

        return await SendDeviceRequestAsync(camera, body, timeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OnvifOperationResult> SetTimeToNtpModeAsync(
        CameraRecord camera,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var body =
            "<tds:SetSystemDateAndTime xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\">" +
            "<tds:DateTimeType>NTP</tds:DateTimeType>" +
            "</tds:SetSystemDateAndTime>";

        return await SendDeviceRequestAsync(camera, body, timeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OnvifOperationResult> SendDeviceRequestAsync(
        CameraRecord camera,
        string body,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildDeviceEndpoint(camera);

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
            using var httpClient = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(BuildSoapEnvelope(body), Encoding.UTF8, "application/soap+xml"),
            };

            if (!string.IsNullOrWhiteSpace(camera.Username) || !string.IsNullOrWhiteSpace(camera.Password))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{camera.Username}:{camera.Password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            using var response = await httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return OnvifOperationResult.Fail("auth", $"Device rejected credentials. HTTP {(int)response.StatusCode}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return OnvifOperationResult.Fail("protocol", $"Device returned HTTP {(int)response.StatusCode}.");
            }

            if (payload.Contains("<Fault", StringComparison.OrdinalIgnoreCase))
            {
                return OnvifOperationResult.Fail("protocol", "ONVIF SOAP fault returned by device.");
            }

            return OnvifOperationResult.Ok("ONVIF operation completed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OnvifOperationResult.Fail("timeout", "ONVIF request timed out.");
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException)
        {
            return OnvifOperationResult.Fail("network", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return OnvifOperationResult.Fail("network", ex.Message);
        }
        catch (Exception ex)
        {
            return OnvifOperationResult.Fail("unknown", ex.Message);
        }
    }

    private static string BuildDeviceEndpoint(CameraRecord camera)
    {
        return $"http://{camera.Ip}:{camera.Port}/onvif/device_service";
    }

    private static string BuildSoapEnvelope(string body)
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\">" +
            "<s:Body>" +
            body +
            "</s:Body>" +
            "</s:Envelope>";
    }

    private static string BuildOnvifTimezone(TimeSpan offset)
    {
        // Many IPCAM vendors expect ONVIF TZ in POSIX-style (sign inverted):
        // UTC+08:00 => CST-8:00:00
        var posixSign = offset >= TimeSpan.Zero ? '-' : '+';
        var absolute = offset.Duration();
        return $"CST{posixSign}{absolute.Hours}:{absolute.Minutes:00}:00";
    }
}
