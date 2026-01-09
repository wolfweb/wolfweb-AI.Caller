using AI.Caller.Core.CallAutomation;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AI.Caller.Core.Services;

/// <summary>
/// 通用DTMF服务接口
/// </summary>
public interface IDtmfService {
    /// <summary>
    /// 开始收集DTMF输入（简单配置）
    /// </summary>
    Task<string> StartCollectionAsync(
        string callId,
        int maxLength = 18,
        char terminationKey = '#',
        char backspaceKey = '*',
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    /// <summary>
    /// 开始收集DTMF输入（详细配置）
    /// </summary>
    Task<string> StartCollectionWithConfigAsync(
        string callId,
        DtmfCollectionConfig config,
        CancellationToken ct = default);

    /// <summary>
    /// 开始收集DTMF输入并返回详细结果
    /// </summary>
    Task<DtmfCollectionResult> StartCollectionWithResultAsync(
        string callId,
        DtmfCollectionConfig config,
        CancellationToken ct = default);

    /// <summary>
    /// 停止收集DTMF输入
    /// </summary>
    Task StopCollectionAsync(string callId);

    /// <summary>
    /// 重置DTMF收集器
    /// </summary>
    Task ResetCollectionAsync(string callId);

    /// <summary>
    /// 获取当前输入
    /// </summary>
    Task<string> GetCurrentInputAsync(string callId);

    /// <summary>
    /// 检查是否正在收集DTMF
    /// </summary>
    Task<bool> IsCollectingAsync(string callId);

    /// <summary>
    /// 获取收集配置
    /// </summary>
    Task<DtmfCollectionConfig?> GetConfigAsync(string callId);

    /// <summary>
    /// 处理接收到的DTMF按键
    /// </summary>
    void OnDtmfToneReceived(string callId, byte tone);
}

/// <summary>
/// DTMF收集配置
/// </summary>
public class DtmfCollectionConfig {
    public const int DefaultMaxLength = 18;
    public const int MinMaxLength = 1;
    public const int MaxMaxLength = 50;
    public const int MinTimeoutSeconds = 1;
    public const int MaxTimeoutMinutes = 10;
    
    private int _maxLength = DefaultMaxLength;
    
    public int MaxLength { 
        get => _maxLength; 
        set => _maxLength = value >= MinMaxLength && value <= MaxMaxLength ? value : 
               throw new ArgumentException($"MaxLength must be between {MinMaxLength} and {MaxMaxLength}"); 
    }
    
    public char TerminationKey { get; set; } = '#';
    public char BackspaceKey { get; set; } = '*';
    
    private TimeSpan? _timeout;
    public TimeSpan? Timeout { 
        get => _timeout; 
        set => _timeout = value.HasValue && value.Value.TotalSeconds >= MinTimeoutSeconds && value.Value.TotalMinutes <= MaxTimeoutMinutes ? value : 
               value.HasValue ? throw new ArgumentException($"Timeout must be between {MinTimeoutSeconds} second and {MaxTimeoutMinutes} minutes") : null; 
    }
    
    public bool EnableLogging { get; set; } = true;
    public string? Description { get; set; }
    
    /// <summary>
    /// 输入映射（例如：* -> X）
    /// </summary>
    public Dictionary<char, char>? InputMapping { get; set; }

    /// <summary>
    /// 验证配置是否有效
    /// </summary>
    public void Validate() {
        if (TerminationKey == BackspaceKey) {
            throw new ArgumentException("TerminationKey and BackspaceKey cannot be the same");
        }
    }
}

/// <summary>
/// DTMF收集结果
/// </summary>
public class DtmfCollectionResult {
    public string Input { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsTimeout { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// 通用DTMF服务实现
/// </summary>
public class DtmfService : IDtmfService {
    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, DtmfCollector> _collectors = new();
    private readonly ConcurrentDictionary<string, DtmfCollectionConfig> _configs = new();

    public DtmfService(ILogger<DtmfService> logger, ILoggerFactory loggerFactory) {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<string> StartCollectionAsync(
        string callId,
        int maxLength = 18,
        char terminationKey = '#',
        char backspaceKey = '*',
        TimeSpan? timeout = null,
        CancellationToken ct = default) {
        
        var config = new DtmfCollectionConfig {
            MaxLength = maxLength,
            TerminationKey = terminationKey,
            BackspaceKey = backspaceKey,
            Timeout = timeout
        };

        return await StartCollectionWithConfigAsync(callId, config, ct);
    }

    public async Task<string> StartCollectionWithConfigAsync(
        string callId,
        DtmfCollectionConfig config,
        CancellationToken ct = default) {
        
        // 验证配置
        config.Validate();
        
        // 创建新的收集器
        var collectorLogger = _loggerFactory.CreateLogger<DtmfCollector>();
        var newCollector = new DtmfCollector(collectorLogger);
        
        // 尝试原子性地添加
        if (!_collectors.TryAdd(callId, newCollector)) {
            try {
                newCollector.Reset();
            } catch (Exception ex) {
                _logger.LogWarning(ex, "清理未使用的DTMF收集器时发生异常");
            }
            throw new InvalidOperationException($"CallId {callId} 已经在进行DTMF收集，请先停止当前收集");
        }
        
        var collector = newCollector;
        _configs[callId] = config;

        _logger.LogInformation("开始DTMF收集: {CallId}, 配置: {Config}",  callId, System.Text.Json.JsonSerializer.Serialize(config));

        try {
            var result = await collector.CollectAsync(
                config.MaxLength,
                config.TerminationKey,
                config.BackspaceKey,
                config.Timeout,
                ct,
                config.InputMapping);

            _logger.LogInformation("DTMF收集完成: {CallId}, 输入长度: {Length}",  callId, result.Length);

            return result;
        } catch (Exception ex) {
            _logger.LogError(ex, "DTMF收集失败: {CallId}", callId);
            throw;
        } finally {
            if (_collectors.TryRemove(callId, out var removedCollector)) {
                try {
                    removedCollector.Reset();
                    _logger.LogDebug("DTMF收集器已清理: {CallId}", callId);
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "清理DTMF收集器时发生异常: {CallId}", callId);
                }
            }
            _configs.TryRemove(callId, out _);
        }
    }

    public Task StopCollectionAsync(string callId) {
        if (_collectors.TryRemove(callId, out var collector)) {
            collector.Reset();
            _configs.TryRemove(callId, out _);
            _logger.LogInformation("DTMF收集已停止: {CallId}", callId);
        }
        return Task.CompletedTask;
    }

    public Task ResetCollectionAsync(string callId) {
        if (_collectors.TryGetValue(callId, out var collector)) {
            collector.Reset();
            _logger.LogInformation("DTMF收集器已重置: {CallId}", callId);
        }
        return Task.CompletedTask;
    }

    public Task<string> GetCurrentInputAsync(string callId) {
        if (_collectors.TryGetValue(callId, out var collector)) {
            return Task.FromResult(collector.GetCurrentInput());
        }
        return Task.FromResult(string.Empty);
    }

    public void OnDtmfToneReceived(string callId, byte tone) {
        if (_collectors.TryGetValue(callId, out var collector)) {
            collector.OnDtmfReceived(tone);
            
            if (_configs.TryGetValue(callId, out var config) && config.EnableLogging) {
                _logger.LogDebug("DTMF按键接收: {CallId}, 按键: {Tone}", callId, tone);
            }
        }
    }

    public Task<bool> IsCollectingAsync(string callId) {
        return Task.FromResult(_collectors.ContainsKey(callId));
    }

    public Task<DtmfCollectionConfig?> GetConfigAsync(string callId) {
        _configs.TryGetValue(callId, out var config);
        return Task.FromResult(config);
    }

    public async Task<DtmfCollectionResult> StartCollectionWithResultAsync(
        string callId,
        DtmfCollectionConfig config,
        CancellationToken ct = default) {
        
        var startTime = DateTime.UtcNow;
        var result = new DtmfCollectionResult {
            StartTime = startTime,
            IsCompleted = false,
            IsTimeout = false,
            IsCancelled = false
        };

        try {
            result.Input = await StartCollectionWithConfigAsync(callId, config, ct);
            result.IsCompleted = true;
        } catch (TimeoutException) {
            result.IsTimeout = true;
        } catch (OperationCanceledException) {
            result.IsCancelled = true;
        } finally {
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }
}