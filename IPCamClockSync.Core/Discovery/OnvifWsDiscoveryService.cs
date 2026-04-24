using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace IPCamClockSync.Core.Discovery;

public sealed class OnvifWsDiscoveryService : IOnvifDiscoveryService
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.255.255.250");

    public async Task<IReadOnlyList<DiscoveredCamera>> DiscoverAsync(DiscoveryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Normalize();

        var multicastEndpoint = new IPEndPoint(IPAddress.Parse(options.MulticastAddress), options.Port);
        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, options.Port);
        var probeSequence = WsDiscoveryMessageBuilder.BuildProbeSequence(multicastEndpoint, broadcastEndpoint).ToList();

        var results = new List<DiscoveredCamera>();
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gate = new object();

        // 使用 options.BindAddresses（若有指定），否則自動偵測所有可用網卡
        var bindAddresses = (options.BindAddresses is { Count: > 0 })
            ? options.BindAddresses
            : GetDiscoveryBindAddresses();

        var timeoutAt = DateTime.UtcNow.AddSeconds(options.ProbeTimeoutSeconds);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sockets = new List<UdpClient>();

        try
        {
            foreach (var bindAddress in bindAddresses)
            {
                var udpClient = CreateClient(bindAddress);
                sockets.Add(udpClient);
            }

            // 先逐一發送所有 probe 消息（每包之間加延遲）
            foreach (var (payload, endpoint, delay) in probeSequence)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var socket in sockets)
                {
                    try { await socket.SendAsync(payload, payload.Length, endpoint); }
                    catch { /* 單一 socket 發送失敗不阻斷其他 */ }
                }
                await Task.Delay(delay, cancellationToken);
            }

            // 並行接收所有 socket 的回應
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
            if (remaining <= TimeSpan.Zero) break;

            try
            {
                var receiveTask = udpClient.ReceiveAsync(cancellationToken).AsTask();
                var delayTask = Task.Delay(remaining, cancellationToken);
                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed != receiveTask) break;

                var received = await receiveTask;
                var xml = Encoding.UTF8.GetString(received.Buffer);

                // 傳入 remoteAddress 作為備援，當 XML 無 IP 可解析時使用
                foreach (var camera in WsDiscoveryMessageParser.ParseProbeMatches(xml, received.RemoteEndPoint.Address))
                {
                    var key = string.IsNullOrWhiteSpace(camera.Address) ? camera.ServiceAddress : camera.Address;
                    if (string.IsNullOrWhiteSpace(key)) continue;

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
            catch
            {
                // 單筆回應解析失敗，略過繼續收
            }
        }
    }

    private static UdpClient CreateClient(IPAddress bindAddress)
    {
        var udpClient = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            // Socket 優化
            udpClient.Client.ExclusiveAddressUse = false;
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 1024 * 1024);
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, 256 * 1024);
            udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, bindAddress.GetAddressBytes());
            udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);

            udpClient.EnableBroadcast = true;
            udpClient.MulticastLoopback = false;

            udpClient.Client.Bind(new IPEndPoint(bindAddress, 0));

            // 加入組播群組才能正確接收 multicast 回應
            udpClient.JoinMulticastGroup(MulticastAddress, bindAddress);
        }
        catch
        {
            udpClient.Dispose();
            throw;
        }

        return udpClient;
    }

    public static IReadOnlyList<(IPAddress Address, string Name)> GetAvailableNetworkInterfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Where(n => n.Supports(NetworkInterfaceComponent.IPv4))
            .Where(n => n.SupportsMulticast)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Where(a => !IPAddress.IsLoopback(a.Address))
                .Select(a => (a.Address, n.Name)))
            .Distinct()
            .OrderBy(x => x.Address.ToString())
            .ToList();
    }

    private static IReadOnlyList<IPAddress> GetDiscoveryBindAddresses()
    {
        var addresses = GetAvailableNetworkInterfaces()
            .Select(x => x.Address)
            .ToArray();

        return addresses.Length > 0 ? addresses : new[] { IPAddress.Any };
    }
}
