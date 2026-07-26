namespace FluxRead.Services;

/// <summary>
/// A resumable stop signal a worker loop checks between items. Starts open; <see cref="Pause"/>
/// blocks the next <see cref="WaitIfPaused"/> until <see cref="Resume"/>. Cancellation still wins.
/// </summary>
public sealed class PauseGate : IDisposable
{
    private readonly ManualResetEventSlim _open = new(initialState: true);

    /// <summary>Gets a value indicating whether the gate is currently holding the loop.</summary>
    public bool IsPaused => !_open.IsSet;

    /// <summary>Holds the loop at its next checkpoint. Idempotent.</summary>
    public void Pause() => _open.Reset();

    /// <summary>Releases a held loop. Idempotent.</summary>
    public void Resume() => _open.Set();

    /// <summary>Blocks while paused; returns immediately when open.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public void WaitIfPaused(CancellationToken cancellationToken) => _open.Wait(cancellationToken);

    public void Dispose() => _open.Dispose();
}
