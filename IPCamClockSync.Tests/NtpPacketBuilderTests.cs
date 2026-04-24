using IPCamClockSync.NtpServer;

namespace IPCamClockSync.Tests;

public class NtpPacketBuilderTests
{
    [Fact]
    public void CreateResponse_ShouldReturn48BytesAndServerMode()
    {
        var request = new byte[48];
        request[0] = 0x1B;
        request[2] = 6;
        request[40] = 0xAA;

        var now = DateTimeOffset.UtcNow;
        var response = NtpPacketBuilder.CreateResponse(request, now, now);

        Assert.Equal(48, response.Length);
        Assert.Equal((byte)0x24, response[0]);
        Assert.Equal((byte)2, response[1]);
        Assert.Equal((byte)6, response[2]);
    }

    [Fact]
    public void CreateResponse_ShouldCopyOriginateTimestampFromRequest()
    {
        var request = new byte[48];
        for (var i = 0; i < 8; i++)
        {
            request[40 + i] = (byte)(i + 1);
        }

        var now = DateTimeOffset.UtcNow;
        var response = NtpPacketBuilder.CreateResponse(request, now, now);

        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(request[40 + i], response[24 + i]);
        }
    }
}
