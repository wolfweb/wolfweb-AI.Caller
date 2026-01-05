using System.Diagnostics;

namespace AI.Caller.Core.CallAutomation;

public interface IFrameTimer {
    void Reset();
    Task WaitForNextFrameAsync(CancellationToken cancellationToken);
}

public class HighPrecisionFrameTimer : IFrameTimer, IDisposable {
    private readonly double _ticksPerFrame;
    private readonly Stopwatch _stopwatch;
    private long _startTimestamp;
    private long _framesSent;
    private bool _isRunning;

    private readonly long _maxDriftTicks;

    public HighPrecisionFrameTimer(int frameIntervalMs) {
        if (frameIntervalMs <= 0) throw new ArgumentException("Interval must be > 0");

        _ticksPerFrame = frameIntervalMs * (Stopwatch.Frequency / 1000.0);
        _stopwatch = new Stopwatch();

        _maxDriftTicks = (long)(_ticksPerFrame * 2);
    }

    public void Reset() {
        _stopwatch.Restart();
        _startTimestamp = _stopwatch.ElapsedTicks;
        _framesSent = 0;
        _isRunning = true;
    }

    public async Task WaitForNextFrameAsync(CancellationToken ct) {
        if (!_isRunning) Reset();

        _framesSent++;

        long targetTicks = _startTimestamp + (long)(_framesSent * _ticksPerFrame);
        long currentTicks = _stopwatch.ElapsedTicks;
        long ticksToWait = targetTicks - currentTicks;

        if (ticksToWait < -_maxDriftTicks) {
            _startTimestamp = currentTicks;
            _framesSent = 0;
            targetTicks = _startTimestamp + (long)(_framesSent * _ticksPerFrame);
            ticksToWait = targetTicks - currentTicks;  // ≈ 0
        }

        if (ticksToWait <= 0) {
            return;
        }

        double millisToWait = ticksToWait * 1000.0 / Stopwatch.Frequency;
        if (millisToWait > 3.0) {
            int delayMs = (int)(millisToWait - 1.5);
            if (delayMs > 0) {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        SpinWait spin = new();
        while (_stopwatch.ElapsedTicks < targetTicks) {
            if (ct.IsCancellationRequested)
                return;
            spin.SpinOnce();
        }
    }

    public void Dispose() {
        _stopwatch.Stop();
        _isRunning = false;
    }
}
