using System.Diagnostics;

namespace AI.Caller.Core.CallAutomation;

public interface IFrameTimer {
    void Reset();
    Task WaitForNextFrameAsync(CancellationToken cancellationToken);
}

public class HighPrecisionFrameTimer : IFrameTimer, IDisposable {
    private readonly double _ticksPerFrame; // 使用 double 保存高精度间隔
    private readonly Stopwatch _stopwatch;
    private long _startTimestamp;
    private long _framesSent;
    private bool _isRunning;

    /// <summary>
    /// 初始化严格帧定时器
    /// </summary>
    /// <param name="frameIntervalMs">固定帧间隔（如 20ms），初始化后不可变</param>
    public HighPrecisionFrameTimer(int frameIntervalMs) {
        if (frameIntervalMs <= 0) throw new ArgumentException("Interval must be > 0");

        _ticksPerFrame = frameIntervalMs * (Stopwatch.Frequency / 1000.0);
        _stopwatch = new Stopwatch();
    }

    /// <summary>
    /// 启动或重置定时器
    /// </summary>
    public void Reset() {
        _stopwatch.Restart();
        _startTimestamp = _stopwatch.ElapsedTicks;
        _framesSent = 0;
        _isRunning = true;
    }

    /// <summary>
    /// 等待下一帧的发送时刻
    /// </summary>
    public async Task WaitForNextFrameAsync(CancellationToken ct) {
        if (!_isRunning) Reset();

        _framesSent++;

        long targetTicks = _startTimestamp + (long)(_framesSent * _ticksPerFrame);

        while (true) {
            long currentTicks = _stopwatch.ElapsedTicks;
            long ticksToWait = targetTicks - currentTicks;

            if (ticksToWait <= 0) break;

            double millisToWait = ticksToWait * 1000.0 / Stopwatch.Frequency;

            if (millisToWait > 3.0) {
                int delayMs = (int)(millisToWait - 2.0);
                if (delayMs > 0) {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
            } else {
                var spin = new SpinWait();
                while (_stopwatch.ElapsedTicks < targetTicks) {
                    spin.SpinOnce();
                }
                break;
            }
        }
    }

    public void Dispose() {
        _stopwatch.Stop();
        _isRunning = false;
    }
}