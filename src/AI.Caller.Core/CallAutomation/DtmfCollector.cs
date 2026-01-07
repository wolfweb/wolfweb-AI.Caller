using Microsoft.Extensions.Logging;
using System.Text;

namespace AI.Caller.Core.CallAutomation;

/// <summary>
/// DTMF按键收集器
/// </summary>
public class DtmfCollector {
    private readonly ILogger _logger;
    private readonly StringBuilder _inputBuffer = new();
    private readonly object _lock = new();
    private TaskCompletionSource<string>? _completionSource;
    private CancellationTokenSource? _timeoutCts;
    private CancellationTokenSource? _linkedCts; // 用于链接外部 ct
    private CancellationToken _externalCt; // 外部传入的取消令牌

    private int _maxLength;
    private char _terminationKey;
    private char _backspaceKey;
    private TimeSpan _timeout;

    public DtmfCollector(ILogger<DtmfCollector> logger) {
        _logger = logger;
    }

    /// <summary>
    /// 开始收集DTMF输入
    /// </summary>
    /// <param name="maxLength">最大长度</param>
    /// <param name="terminationKey">终止键（默认#）</param>
    /// <param name="backspaceKey">退格键（默认*）</param>
    /// <param name="timeout">超时时间（每次有效按键后重置）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>收集到的输入</returns>
    public async Task<string> CollectAsync(int maxLength, char terminationKey = '#', char backspaceKey = '*', TimeSpan? timeout = null, CancellationToken ct = default) {
        lock (_lock) {
            if (_completionSource != null) {
                throw new InvalidOperationException("DTMF收集已在进行中");
            }

            _maxLength = maxLength;
            _terminationKey = terminationKey;
            _backspaceKey = backspaceKey;
            _timeout = timeout ?? TimeSpan.Zero;
            _inputBuffer.Clear();
            _completionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (timeout.HasValue) {
                _timeoutCts = new CancellationTokenSource();
                _externalCt = ct;
                _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _timeoutCts.Token);
                StartTimeoutTimer();
            } else {
                _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            }
        }

        _logger.LogInformation("开始收集DTMF输入，最大长度: {MaxLength}, 终止键: {TermKey}, 退格键: {BackspaceKey}, 超时: {Timeout}s",maxLength, terminationKey, backspaceKey, timeout?.TotalSeconds ?? 0);

        try {
            using var registration = ct.Register(() => {
                _logger.LogInformation("DTMF收集被取消");
                CompleteWithCancellation(ct);
            });

            var result = await _completionSource.Task.ConfigureAwait(false);
            _logger.LogInformation("DTMF收集完成，输入: {Input}", MaskInput(result));
            return result;
        } finally {
            Cleanup();
        }
    }

    /// <summary>
    /// 处理接收到的DTMF按键
    /// </summary>
    /// <param name="tone">DTMF按键（0-9, *, #, A-D）</param>
    public void OnDtmfReceived(byte tone) {
        lock (_lock) {
            if (_completionSource == null || _completionSource.Task.IsCompleted) {
                _logger.LogDebug("收到DTMF但收集已结束或未开始: {Tone}", tone);
                return;
            }

            char key = ConvertToneToChar(tone);
            _logger.LogDebug("收到DTMF按键: {Key} (tone: {Tone})", key, tone);

            // 处理终止键
            if (key == _terminationKey) {
                var input = _inputBuffer.ToString();
                _logger.LogInformation("收到终止键，完成输入: {Input}", MaskInput(input));
                _completionSource.TrySetResult(input);
                return;
            }

            // 处理退格键
            if (key == _backspaceKey) {
                if (_inputBuffer.Length > 0) {
                    _inputBuffer.Length--;
                    _logger.LogDebug("退格，当前长度: {Length}", _inputBuffer.Length);
                }

                ResetTimeoutTimer();
                return;
            }

            // 普通数字键
            if (_inputBuffer.Length < _maxLength) {
                _inputBuffer.Append(key);
                _logger.LogDebug("添加按键，当前长度: {Length}/{MaxLength}", _inputBuffer.Length, _maxLength);
                ResetTimeoutTimer();
            } else {
                _logger.LogWarning("输入已达最大长度，忽略按键: {Key}", key);
            }
        }
    }

    /// <summary>
    /// 重置超时计时器（每次有效按键时调用）
    /// </summary>
    private void ResetTimeoutTimer() {
        if (_timeout <= TimeSpan.Zero) return;

        lock (_lock) {
            _timeoutCts?.Cancel();
            _timeoutCts?.Dispose();
            _timeoutCts = new CancellationTokenSource();

            _linkedCts?.Dispose();
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_externalCt, _timeoutCts.Token);

            StartTimeoutTimer();
        }
    }

    /// <summary>
    /// 启动一次超时计时
    /// </summary>
    private void StartTimeoutTimer() {
        if (_timeout <= TimeSpan.Zero) return;

        _ = Task.Delay(_timeout, _linkedCts!.Token)
            .ContinueWith(t => {
                if (t.IsCanceled) return;

                lock (_lock) {
                    if (_completionSource != null && !_completionSource.Task.IsCompleted) {
                        _logger.LogWarning("DTMF输入超时（{Timeout}s 无新按键）", _timeout.TotalSeconds);
                        _completionSource.TrySetException(new TimeoutException("DTMF输入超时"));
                    }
                }
            }, TaskScheduler.Default);
    }

    /// <summary>
    /// 外部取消时的统一处理
    /// </summary>
    private void CompleteWithCancellation(CancellationToken ct) {
        lock (_lock) {
            _completionSource?.TrySetCanceled(ct);
        }
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    private void Cleanup() {
        lock (_lock) {
            _timeoutCts?.Cancel();
            _timeoutCts?.Dispose();
            _timeoutCts = null;

            _linkedCts?.Dispose();
            _linkedCts = null;

            _completionSource = null;
        }
    }

    /// <summary>
    /// 手动重置收集器（强制结束当前收集）
    /// </summary>
    public void Reset() {
        lock (_lock) {
            _inputBuffer.Clear();

            if (_completionSource != null && !_completionSource.Task.IsCompleted) {
                _completionSource.TrySetCanceled();
            }

            Cleanup();
            _logger.LogDebug("DTMF收集器已手动重置");
        }
    }

    /// <summary>
    /// 获取当前已收集的输入（不包含终止键）
    /// </summary>
    public string GetCurrentInput() {
        lock (_lock) {
            return _inputBuffer.ToString();
        }
    }

    /// <summary>
    /// Tone 转字符
    /// </summary>
    private static char ConvertToneToChar(byte tone) {
        return tone switch {
            0 => '0',
            1 => '1',
            2 => '2',
            3 => '3',
            4 => '4',
            5 => '5',
            6 => '6',
            7 => '7',
            8 => '8',
            9 => '9',
            10 => '*',
            11 => '#',
            12 => 'A',
            13 => 'B',
            14 => 'C',
            15 => 'D',
            _ => '?'
        };
    }

    /// <summary>
    /// 日志脱敏
    /// </summary>
    private static string MaskInput(string input) {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        if (input.Length <= 4)
            return new string('*', input.Length);

        return $"{input.Substring(0, 2)}{new string('*', input.Length - 4)}{input.Substring(input.Length - 2)}";
    }
}