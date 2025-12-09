using AI.Caller.Core.CallAutomation;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core;

/// <summary>
/// AIAutoResponder - DTMF收集功能扩展
/// </summary>
public sealed partial class AIAutoResponder {
    private DtmfCollector? _dtmfCollector;
    private object? _dtmfInputService; // IDtmfInputService，使用object避免Core层依赖Phone层
    private string? _currentCallId;

    /// <summary>
    /// 设置DTMF收集器
    /// </summary>
    public void SetDtmfCollector(DtmfCollector dtmfCollector) {
        _dtmfCollector = dtmfCollector;
        _logger.LogDebug("DtmfCollector已设置");
    }

    /// <summary>
    /// 设置DTMF输入服务（用于保存到数据库）
    /// </summary>
    /// <param name="dtmfInputService">IDtmfInputService实例</param>
    public void SetDtmfInputService(object? dtmfInputService) {
        _dtmfInputService = dtmfInputService;
        _logger.LogDebug("DtmfInputService已设置");
    }

    /// <summary>
    /// 设置当前通话上下文（用于关联数据库记录）
    /// </summary>
    /// <param name="callId">通话ID</param>
    public void SetCallContext(string callId) {
        _currentCallId = callId;
        _logger.LogDebug("CallContext已设置: {CallId}", callId);
    }

    /// <summary>
    /// 收集DTMF输入
    /// </summary>
    /// <param name="maxLength">最大长度</param>
    /// <param name="terminationKey">终止键</param>
    /// <param name="backspaceKey">退格键</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>收集到的输入</returns>
    public async Task<string> CollectDtmfInputAsync(
        int maxLength,
        char terminationKey = '#',
        char backspaceKey = '*',
        TimeSpan? timeout = null,
        CancellationToken ct = default) {
        if (_dtmfCollector == null) {
            _logger.LogError("DtmfCollector未设置，无法收集DTMF输入");
            throw new InvalidOperationException("DtmfCollector未设置");
        }

        _logger.LogInformation("开始收集DTMF输入，最大长度: {MaxLength}", maxLength);

        try {
            var input = await _dtmfCollector.CollectAsync(
                maxLength,
                terminationKey,
                backspaceKey,
                timeout,
                ct);

            _logger.LogInformation("DTMF输入收集完成");
            return input;
        } catch (TimeoutException) {
            _logger.LogWarning("DTMF输入超时");
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "DTMF输入收集失败");
            throw;
        }
    }

    /// <summary>
    /// 处理DTMF按键（由SIPClient调用）
    /// </summary>
    public void OnDtmfToneReceived(byte tone) {
        _dtmfCollector?.OnDtmfReceived(tone);
    }
}
