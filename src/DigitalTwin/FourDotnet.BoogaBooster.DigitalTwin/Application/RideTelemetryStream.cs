using System.Runtime.CompilerServices;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;
using FourDotnet.BoogaBooster.DigitalTwin.Domain;

namespace FourDotnet.BoogaBooster.DigitalTwin.Application;

/// <summary>
/// Produces the ride's telemetry broadcast: a stream of full <see cref="RideTelemetry"/>
/// snapshots sampled from the store at the fixed telemetry rate
/// (<see cref="RideParameters.TelemetryInterval"/>), decoupled from the physics
/// timestep. Frames are emitted whenever the ride is active (any state except
/// <see cref="RideState.Idle"/>), so boarding and loading are observable and not only
/// the running ride; while the ride is idle the loop keeps ticking (holding the
/// connection open) but yields nothing. Timing runs off an injected
/// <see cref="TimeProvider"/> so the loop is deterministic under a fake clock in
/// tests. Each connection samples independently; there is no shared broadcaster state.
/// </summary>
public sealed class RideTelemetryStream
{
    private readonly IRideStore _store;
    private readonly TimeProvider _timeProvider;

    public RideTelemetryStream(IRideStore store, TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Streams telemetry frames until the client disconnects (the token is cancelled).
    /// Emits one snapshot per <see cref="RideParameters.TelemetryInterval"/> while the
    /// ride is active (any state except <see cref="RideState.Idle"/>); stays quiet (but
    /// alive) while the ride is idle.
    /// </summary>
    public async IAsyncEnumerable<RideTelemetry> Stream(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_store.IsActive)
            {
                yield return _store.GetTelemetry();
            }

            try
            {
                await Task.Delay(RideParameters.TelemetryInterval, _timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }
}
