using System.Net;
using System.Net.NetworkInformation;
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
        var gate = new object();
        var multicastEndpoint = new IPEndPoint(IPAddress.Parse(options.MulticastAddress), options.Port);
        var timeoutAt = DateTime.UtcNow.AddSeconds(options.ProbeTimeoutSeconds);
        var bindAddresses = GetDiscoveryBindAddresses();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sockets = new List<UdpClient>();

        try
        {
            foreach (var bindAddress in bindAddresses)
            {
                var udpClient = CreateClient(bindAddress);
                sockets.Add(udpClient);
                await udpClient.SendAsync(payload, payload.Length, multicastEndpoint);
            }

            var receiveTasks = sockets
                .Select(socket => ReceiveResponsesAsync(socket, timeoutAt, results, dedupe, gate, linkedCts.Token))
                .ToArray();

            await Task.WhenAll(receiveTasks);
            return results;
        }
        finally
        {
            linkedCts.Cancel();
            foreach (var socket in sockets)
            {
                socket.Dispose();
            }
        }
    }

    private static async Task ReceiveResponsesAsync(
        UdpClient udpClient,
        DateTime timeoutAt,
        List<DiscoveredCamera> results,
        HashSet<string> dedupe,
        object gate,
        CancellationToken cancellationToken)
    {
        while (DateTime.UtcNow < timeoutAt && !cancellationToken.IsCancellationRequested)
        {
            var remaining = timeoutAt - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            try
            {
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
                    lock (gate)
                    {
                        if (dedupe.Add(key))
                        {
                            results.Add(camera);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private static UdpClient CreateClient(IPAddress bindAddress)
    {
        var udpClient = new UdpClient(new IPEndPoint(bindAddress, 0));
        udpClient.EnableBroadcast = true;
        udpClient.MulticastLoopback = false;
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, bindAddress.GetAddressBytes());
        return udpClient;
    }

    private static IReadOnlyList<IPAddress> GetDiscoveryBindAddresses()
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .Where(network => network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Where(network => network.Supports(NetworkInterfaceComponent.IPv4))
            .Where(network => network.SupportsMulticast)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address)
            .Where(address => !IPAddress.IsLoopback(address))
            .Distinct()
            .ToArray();

        return addresses.Length > 0 ? addresses : new[] { IPAddress.Any };
    }
}
