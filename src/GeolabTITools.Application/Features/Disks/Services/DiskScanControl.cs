namespace GeolabTITools.Application.Features.Disks.Services;

/// <summary>Portão assíncrono usado para pausar e retomar uma varredura sem bloquear uma thread.</summary>
public sealed class DiskScanControl
{
    private readonly object _sync = new();
    private TaskCompletionSource<bool> _resumeSignal = CompletedSignal();
    private bool _isPaused;

    public bool IsPaused
    {
        get { lock (_sync) return _isPaused; }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_isPaused) return;
            _isPaused = true;
            _resumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource<bool> signal;
        lock (_sync)
        {
            if (!_isPaused) return;
            _isPaused = false;
            signal = _resumeSignal;
            _resumeSignal = CompletedSignal();
        }
        signal.TrySetResult(true);
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task wait;
        lock (_sync) wait = _isPaused ? _resumeSignal.Task : Task.CompletedTask;
        return wait.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource<bool> CompletedSignal()
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult(true);
        return signal;
    }
}
