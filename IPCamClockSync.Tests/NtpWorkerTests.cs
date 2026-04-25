using System.Net;
using System.Net.Sockets;
using IPCamClockSync.NtpServer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IPCamClockSync.Tests;

public class NtpWorkerTests
{
    [Fact]
    public async Task Worker_WhenReceiveValidNtpPacket_ShouldReply48Bytes()
    {
        var port = GetFreeUdpPort();
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var worker = new Worker(
            loggerFactory.CreateLogger<Worker>(),
            Options.Create(new NtpServerOptions { BindAddress = "127.0.0.1", Port = port }));

        await worker.StartAsync(CancellationToken.None);

        try
        {
            await Task.Delay(120);

            using var client = new UdpClient();
            var request = new byte[48];
            request[0] = 0x1B;
            request[2] = 6;

            await client.SendAsync(request, request.Length, new IPEndPoint(IPAddress.Loopback, port));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await client.ReceiveAsync(cts.Token);

            Assert.Equal(48, response.Buffer.Length);
            Assert.Equal((byte)0x24, response.Buffer[0]);
            Assert.Equal((byte)2, response.Buffer[1]);
            Assert.Equal((byte)6, response.Buffer[2]);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task Worker_WhenReceiveInvalidPacket_ShouldNotReply()
    {
        var port = GetFreeUdpPort();
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var worker = new Worker(
            loggerFactory.CreateLogger<Worker>(),
            Options.Create(new NtpServerOptions { BindAddress = "127.0.0.1", Port = port }));

        await worker.StartAsync(CancellationToken.None);

        try
        {
            await Task.Delay(120);

            using var client = new UdpClient();
            var invalidPacket = new byte[10];

            await client.SendAsync(invalidPacket, invalidPacket.Length, new IPEndPoint(IPAddress.Loopback, port));

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await client.ReceiveAsync(cts.Token);
            });
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    private static int GetFreeUdpPort()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }
}
