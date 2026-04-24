namespace IPCamClockSync.NtpServer;

public static class NtpPacketBuilder
{
    private static readonly DateTimeOffset NtpEpoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static byte[] CreateResponse(byte[] request, DateTimeOffset receiveTimeUtc, DateTimeOffset transmitTimeUtc)
    {
        var response = new byte[48];

        // LI=0, VN=4, Mode=4 (server)
        response[0] = 0x24;
        response[1] = 2; // stratum
        response[2] = request.Length > 2 ? request[2] : (byte)6; // poll interval
        response[3] = 0xEC; // precision ~= 2^-20

        // Root delay and root dispersion (fixed-point, here set to small values)
        response[4] = 0x00;
        response[5] = 0x00;
        response[6] = 0x01;
        response[7] = 0x00;
        response[8] = 0x00;
        response[9] = 0x00;
        response[10] = 0x01;
        response[11] = 0x00;

        // Reference ID ("LOCL")
        response[12] = 0x4C;
        response[13] = 0x4F;
        response[14] = 0x43;
        response[15] = 0x4C;

        WriteTimestamp(response, 16, receiveTimeUtc);

        // Originate timestamp = client transmit timestamp (from request)
        if (request.Length >= 48)
        {
            Buffer.BlockCopy(request, 40, response, 24, 8);
        }

        WriteTimestamp(response, 32, receiveTimeUtc);
        WriteTimestamp(response, 40, transmitTimeUtc);

        return response;
    }

    public static void WriteTimestamp(byte[] buffer, int offset, DateTimeOffset utcTime)
    {
        var delta = utcTime.ToUniversalTime() - NtpEpoch;
        var totalSeconds = delta.TotalSeconds;

        var seconds = (ulong)Math.Floor(totalSeconds);
        var fraction = (ulong)((totalSeconds - Math.Floor(totalSeconds)) * uint.MaxValue);

        buffer[offset + 0] = (byte)(seconds >> 24);
        buffer[offset + 1] = (byte)(seconds >> 16);
        buffer[offset + 2] = (byte)(seconds >> 8);
        buffer[offset + 3] = (byte)(seconds);

        buffer[offset + 4] = (byte)(fraction >> 24);
        buffer[offset + 5] = (byte)(fraction >> 16);
        buffer[offset + 6] = (byte)(fraction >> 8);
        buffer[offset + 7] = (byte)(fraction);
    }
}
