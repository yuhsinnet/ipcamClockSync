using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IPCamClockSync.Core.Discovery;

public sealed class OnvifWsDiscoveryService : IOnvifDiscoveryService
{
    public async Task<IReadOnlyList<DiscoveredCamera>> DiscoverAsync(DiscoveryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Normalize();
        var message = WsDiscoveryMessageBuilder.BuildProbeMessage(Guid.NewGuid());
        var payload = Encoding.UTF8.GetBytes(message);
        var results = new List<DiscoveredCamera>();
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var udpClient = new UdpClient(AddressFamily.InterNetwork);
        udpClient.EnableBroadcast = true;
        udpClient.MulticastLoopback = false;
        await udpClient.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Parse(options.MulticastAddress), options.Port));

        var timeoutAt = DateTime.UtcNow.AddSeconds(options.ProbeTimeoutSeconds);
        while (DateTime.UtcNow < timeoutAt && !cancellationToken.IsCancellationRequested)
        {
            var remaining = timeoutAt - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var receiveTask = udpClient.ReceiveAsync(cancellationToken).AsTask();
            var delayTask = Task.Delay(remaining, cancellationToken);
            var completed = await Task.WhenAny(receiveTask, delayTask);
            if (completed != receiveTask)
            {
                break;
            }

            var received = await receiveTask;
            var xml = Encoding.UTF8.GetString(received.Buffer);
            foreach (var camera in WsDiscoveryMessageParser.ParseProbeMatches(xml))
            {
                var key = string.IsNullOrWhiteSpace(camera.ServiceAddress) ? camera.Address : camera.ServiceAddress;
                if (dedupe.Add(key))
                {
                    results.Add(camera);
                }
            }
        }

        return results;
    }
}
