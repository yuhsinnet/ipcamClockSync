namespace IPCamClockSync.Core.Discovery;

public sealed class StubOnvifDiscoveryService : IOnvifDiscoveryService
{
    public Task<IReadOnlyList<DiscoveredCamera>> DiscoverAsync(DiscoveryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Normalize();
        return Task.FromResult<IReadOnlyList<DiscoveredCamera>>(Array.Empty<DiscoveredCamera>());
    }
}
