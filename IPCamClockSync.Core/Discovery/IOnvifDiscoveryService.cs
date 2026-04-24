namespace IPCamClockSync.Core.Discovery;

public interface IOnvifDiscoveryService
{
    Task<IReadOnlyList<DiscoveredCamera>> DiscoverAsync(DiscoveryOptions options, CancellationToken cancellationToken);
}
