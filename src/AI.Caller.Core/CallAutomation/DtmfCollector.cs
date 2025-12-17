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

    private int _maxLength;
    private char _terminationKey;
    private char _backspaceKey;
    private TimeSpan? _timeout;

    public DtmfCollector(ILogger<DtmfCollector> logger) {
        _logger = logger;
    }

    /// <summary>
    /// 开始收集DTMF输入
    /// </summary>
    /// <param name="maxLength">最大长度</param>
    /// <param name="terminationKey">终止键（默认#）</param>
    /// <param name="backspaceKey">退格键（默认*）</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>收集到的输入</returns>
    public async Task<string> CollectAsync(
        int maxLength,
        char terminationKey = '#',
        char backspaceKey = '*',
        TimeSpan? timeout = null,
        CancellationToken ct = default) {
        lock (_lock) {
            if (_completionSource != null) {
                throw new InvalidOperationException("DTMF收集已在进行中");
            }

            _maxLength = maxLength;
            _terminationKey = terminationKey;
            _backspaceKey = backspaceKey;
            _timeout = timeout;
            _inputBuffer.Clear();
            _completionSource = new TaskCompletionSource<string>();
        }

        _logger.LogInformation("开始收集DTMF输入，最大长度: {MaxLength}, 终止键: {TermKey}, 超时: {Timeout}",
            maxLength, terminationKey, timeout?.TotalSeconds ?? 0);

        try {
            // 设置超时
            if (timeout.HasValue) {
                _timeoutCts = new CancellationTokenSource();
                _ = Task.Delay(timeout.Value, _timeoutCts.Token).ContinueWith(t => {
                    if (!t.IsCanceled) {
                        _logger.LogWarning("DTMF输入超时");
                        _completionSource?.TrySetException(new TimeoutException("DTMF输入超时"));
                    }
                }, TaskScheduler.Default);
            }

            // 等待收集完成或取消
            using var registration = ct.Register(() => {
                _logger.LogInformation("DTMF收集被取消");
                _completionSource?.TrySetCanceled(ct);
            });

            var result = await _completionSource.Task;
            _logger.LogInformation("DTMF收集完成，输入: {Input}", MaskInput(result));
            return result;
        } finally {
            lock (_lock) {
                _timeoutCts?.Cancel();
                _timeoutCts?.Dispose();
                _timeoutCts = null;
                _completionSource = null;
            }
        }
    }

    /// <summary>
    /// 处理接收到的DTMF按键
    /// </summary>
    /// <param name="tone">DTMF按键（0-9, *, #, A-D）</param>
    public void OnDtmfReceived(byte tone) {
        lock (_lock) {
            if (_completionSource == null || _completionSource.Task.IsCompleted) {
                _logger.LogDebug("收到DTMF按键但未在收集状态或已完成: {Tone}", tone);
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
                    _logger.LogDebug("退格，当前输入长度: {Length}", _inputBuffer.Length);
                }
                return;
            }

            // 添加按键到缓冲区
            if (_inputBuffer.Length < _maxLength) {
                _inputBuffer.Append(key);
                _logger.LogDebug("添加按键，当前输入长度: {Length}/{MaxLength}", _inputBuffer.Length, _maxLength);

                // 检查是否达到最大长度
                if (_inputBuffer.Length >= _maxLength) {
                    var input = _inputBuffer.ToString();
                    _logger.LogInformation("达到最大长度，完成输入: {Input}", MaskInput(input));
                    _completionSource.TrySetResult(input);
                }
            } else {
                _logger.LogWarning("输入已达到最大长度，忽略按键: {Key}", key);
            }
        }
    }

    /// <summary>
    /// 重置收集器
    /// </summary>
    public void Reset() {
        lock (_lock) {
            _inputBuffer.Clear();
            _completionSource?.TrySetCanceled();
            _completionSource = null;
            _timeoutCts?.Cancel();
            _timeoutCts?.Dispose();
            _timeoutCts = null;
            _logger.LogDebug("DTMF收集器已重置");
        }
    }

    /// <summary>
    /// 获取当前输入
    /// </summary>
    public string GetCurrentInput() {
        lock (_lock) {
            return _inputBuffer.ToString();
        }
    }

    /// <summary>
    /// 将DTMF tone转换为字符
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
    /// 脱敏输入（用于日志）
    /// </summary>
    private static string MaskInput(string input) {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        if (input.Length <= 4)
            return new string('*', input.Length);

        // 显示前2位和后2位
        return $"{input.Substring(0, 2)}{"".PadLeft(input.Length - 4, '*')}{input.Substring(input.Length - 2)}";
    }
}