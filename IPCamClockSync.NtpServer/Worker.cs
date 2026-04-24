using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace IPCamClockSync.NtpServer;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly NtpServerOptions _options;

    public Worker(ILogger<Worker> logger, IOptions<NtpServerOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IPAddress.TryParse(_options.BindAddress, out var bindAddress))
        {
            bindAddress = IPAddress.Any;
        }

        using var udpClient = new UdpClient(new IPEndPoint(bindAddress, _options.Port));
        _logger.LogInformation("NTP server listening on {BindAddress}:{Port}", bindAddress, _options.Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udpClient.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (received.Buffer.Length < 48)
            {
                _logger.LogWarning("Ignored invalid NTP packet from {RemoteEndPoint}", received.RemoteEndPoint);
                continue;
            }

            var receiveTimeUtc = DateTimeOffset.UtcNow;
            var transmitTimeUtc = DateTimeOffset.UtcNow;
            var response = NtpPacketBuilder.CreateResponse(received.Buffer, receiveTimeUtc, transmitTimeUtc);

            await udpClient.SendAsync(response, response.Length, received.RemoteEndPoint);
            _logger.LogInformation("Replied NTP request from {RemoteEndPoint}", received.RemoteEndPoint);
        }

        _logger.LogInformation("NTP server stopped.");
    }
}
