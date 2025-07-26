using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 音频诊断工具
    /// </summary>
    public interface IAudioDiagnostics
    {
        /// <summary>
        /// 记录音频处理事件
        /// </summary>
        void LogAudioEvent(AudioEventType eventType, AudioSource source, string message, object? context = null);
        
        /// <summary>
        /// 记录音频错误
        /// </summary>
        void LogAudioError(AudioSource source, Exception exception, string operation, object? context = null);
        
        /// <summary>
        /// 记录性能指标
        /// </summary>
        void LogPerformanceMetric(string metricName, double value, string unit, object? context = null);
        
        /// <summary>
        /// 开始性能测量
        /// </summary>
        IDisposable StartPerformanceMeasurement(string operationName, AudioSource? source = null);
        
        /// <summary>
        /// 获取诊断报告
        /// </summary>
        AudioDiagnosticReport GetDiagnosticReport();
        
        /// <summary>
        /// 重置诊断数据
        /// </summary>
        void Reset();
    }
    
    /// <summary>
    /// 音频诊断工具实现
    /// </summary>
    public class AudioDiagnostics : IAudioDiagnostics
    {
        private readonly ILogger _logger;
        private readonly AudioDiagnosticSettings _settings;
        private readonly List<AudioDiagnosticEntry> _diagnosticEntries;
        private readonly Dictionary<string, PerformanceCounter> _performanceCounters;
        private readonly object _lockObject = new object();
        
        public AudioDiagnostics(AudioDiagnosticSettings settings, ILogger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _diagnosticEntries = new List<AudioDiagnosticEntry>();
            _performanceCounters = new Dictionary<string, PerformanceCounter>();
        }
        
        public void LogAudioEvent(AudioEventType eventType, AudioSource source, string message, object? context = null)
        {
            if (!_settings.EnableEventLogging)
                return;
                
            var entry = new AudioDiagnosticEntry
            {
                EventType = eventType,
                Source = source,
                Message = message,
                Context = context,
                Timestamp = DateTime.UtcNow,
                ThreadId = Environment.CurrentManagedThreadId
            };
            
            lock (_lockObject)
            {
                _diagnosticEntries.Add(entry);
                
                // 限制条目数量
                if (_diagnosticEntries.Count > _settings.MaxDiagnosticEntries)
                {
                    _diagnosticEntries.RemoveAt(0);
                }
            }
            
            // 根据事件类型选择日志级别
            var logLevel = eventType switch
            {
                AudioEventType.Error => LogLevel.Error,
                AudioEventType.Warning => LogLevel.Warning,
                AudioEventType.Info => LogLevel.Information,
                AudioEventType.Debug => LogLevel.Debug,
                _ => LogLevel.Trace
            };
            
            var contextJson = context != null ? JsonSerializer.Serialize(context) : null;
            _logger.Log(logLevel, "[{Source}] {EventType}: {Message} {Context}", 
                source, eventType, message, contextJson);
        }
        
        public void LogAudioError(AudioSource source, Exception exception, string operation, object? context = null)
        {
            var errorContext = new
            {
                Operation = operation,
                ExceptionType = exception.GetType().Name,
                StackTrace = _settings.IncludeStackTrace ? exception.StackTrace : null,
                Context = context
            };
            
            LogAudioEvent(AudioEventType.Error, source, exception.Message, errorContext);
            
            // 记录详细的错误信息
            _logger.LogError(exception, "[{Source}] Error in {Operation}: {Message}", 
                source, operation, exception.Message);
        }
        
        public void LogPerformanceMetric(string metricName, double value, string unit, object? context = null)
        {
            if (!_settings.EnablePerformanceLogging)
                return;
                
            lock (_lockObject)
            {
                if (!_performanceCounters.TryGetValue(metricName, out var counter))
                {
                    counter = new PerformanceCounter(metricName, unit);
                    _performanceCounters[metricName] = counter;
                }
                
                counter.AddValue(value);
            }
            
            var perfContext = new
            {
                Metric = metricName,
                Value = value,
                Unit = unit,
                Context = context
            };
            
            LogAudioEvent(AudioEventType.Performance, AudioSource.Mixed, 
                $"{metricName}: {value} {unit}", perfContext);
        }
        
        public IDisposable StartPerformanceMeasurement(string operationName, AudioSource? source = null)
        {
            return new PerformanceMeasurement(this, operationName, source ?? AudioSource.Mixed);
        }
        
        public AudioDiagnosticReport GetDiagnosticReport()
        {
            lock (_lockObject)
            {
                var report = new AudioDiagnosticReport
                {
                    GeneratedAt = DateTime.UtcNow,
                    TotalEntries = _diagnosticEntries.Count,
                    Entries = new List<AudioDiagnosticEntry>(_diagnosticEntries),
                    PerformanceMetrics = new Dictionary<string, PerformanceCounter>(_performanceCounters)
                };
                
                // 统计各种事件类型
                report.EventTypeCounts = _diagnosticEntries
                    .GroupBy(e => e.EventType)
                    .ToDictionary(g => g.Key, g => g.Count());
                    
                // 统计各种音频源
                report.SourceCounts = _diagnosticEntries
                    .GroupBy(e => e.Source)
                    .ToDictionary(g => g.Key, g => g.Count());
                
                return report;
            }
        }
        
        public void Reset()
        {
            lock (_lockObject)
            {
                _diagnosticEntries.Clear();
                _performanceCounters.Clear();
            }
            
            _logger.LogInformation("Audio diagnostics reset");
        }
        
        private class PerformanceMeasurement : IDisposable
        {
            private readonly AudioDiagnostics _diagnostics;
            private readonly string _operationName;
            private readonly AudioSource _source;
            private readonly Stopwatch _stopwatch;
            private bool _disposed = false;
            
            public PerformanceMeasurement(AudioDiagnostics diagnostics, string operationName, AudioSource source)
            {
                _diagnostics = diagnostics;
                _operationName = operationName;
                _source = source;
                _stopwatch = Stopwatch.StartNew();
            }
            
            public void Dispose()
            {
                if (_disposed)
                    return;
                    
                _disposed = true;
                _stopwatch.Stop();
                
                _diagnostics.LogPerformanceMetric(
                    $"{_operationName}_Duration", 
                    _stopwatch.Elapsed.TotalMilliseconds, 
                    "ms",
                    new { Source = _source });
            }
        }
    }
    
    /// <summary>
    /// 性能计数器
    /// </summary>
    public class PerformanceCounter
    {
        public string Name { get; }
        public string Unit { get; }
        public double TotalValue { get; private set; }
        public int Count { get; private set; }
        public double MinValue { get; private set; } = double.MaxValue;
        public double MaxValue { get; private set; } = double.MinValue;
        public double AverageValue => Count > 0 ? TotalValue / Count : 0;
        
        public PerformanceCounter(string name, string unit)
        {
            Name = name;
            Unit = unit;
        }
        
        public void AddValue(double value)
        {
            TotalValue += value;
            Count++;
            
            if (value < MinValue)
                MinValue = value;
                
            if (value > MaxValue)
                MaxValue = value;
        }
        
        public void Reset()
        {
            TotalValue = 0;
            Count = 0;
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;
        }
        
        public override string ToString()
        {
            return $"{Name}: Avg={AverageValue:F2}{Unit}, Min={MinValue:F2}{Unit}, Max={MaxValue:F2}{Unit}, Count={Count}";
        }
    }
    
    /// <summary>
    /// 音频诊断条目
    /// </summary>
    public class AudioDiagnosticEntry
    {
        public AudioEventType EventType { get; set; }
        public AudioSource Source { get; set; }
        public string Message { get; set; } = "";
        public object? Context { get; set; }
        public DateTime Timestamp { get; set; }
        public int ThreadId { get; set; }
    }
    
    /// <summary>
    /// 音频诊断报告
    /// </summary>
    public class AudioDiagnosticReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalEntries { get; set; }
        public List<AudioDiagnosticEntry> Entries { get; set; } = new();
        public Dictionary<string, PerformanceCounter> PerformanceMetrics { get; set; } = new();
        public Dictionary<AudioEventType, int> EventTypeCounts { get; set; } = new();
        public Dictionary<AudioSource, int> SourceCounts { get; set; } = new();
        
        /// <summary>
        /// 导出为JSON
        /// </summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        
        /// <summary>
        /// 生成摘要报告
        /// </summary>
        public string GenerateSummary()
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"Audio Diagnostic Report - Generated at {GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            summary.AppendLine($"Total Entries: {TotalEntries}");
            summary.AppendLine();
            
            // 事件类型统计
            summary.AppendLine("Event Type Counts:");
            foreach (var kvp in EventTypeCounts.OrderByDescending(x => x.Value))
            {
                summary.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
            summary.AppendLine();
            
            // 音频源统计
            summary.AppendLine("Audio Source Counts:");
            foreach (var kvp in SourceCounts.OrderByDescending(x => x.Value))
            {
                summary.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
            summary.AppendLine();
            
            // 性能指标
            summary.AppendLine("Performance Metrics:");
            foreach (var kvp in PerformanceMetrics)
            {
                summary.AppendLine($"  {kvp.Value}");
            }
            
            return summary.ToString();
        }
    }
    
    /// <summary>
    /// 音频诊断设置
    /// </summary>
    public class AudioDiagnosticSettings
    {
        /// <summary>
        /// 启用事件日志记录
        /// </summary>
        public bool EnableEventLogging { get; set; } = true;
        
        /// <summary>
        /// 启用性能日志记录
        /// </summary>
        public bool EnablePerformanceLogging { get; set; } = true;
        
        /// <summary>
        /// 包含堆栈跟踪
        /// </summary>
        public bool IncludeStackTrace { get; set; } = false;
        
        /// <summary>
        /// 最大诊断条目数
        /// </summary>
        public int MaxDiagnosticEntries { get; set; } = 10000;
        
        /// <summary>
        /// 创建默认设置
        /// </summary>
        public static AudioDiagnosticSettings CreateDefault()
        {
            return new AudioDiagnosticSettings();
        }
        
        /// <summary>
        /// 创建详细设置
        /// </summary>
        public static AudioDiagnosticSettings CreateVerbose()
        {
            return new AudioDiagnosticSettings
            {
                EnableEventLogging = true,
                EnablePerformanceLogging = true,
                IncludeStackTrace = true,
                MaxDiagnosticEntries = 50000
            };
        }
        
        /// <summary>
        /// 创建精简设置
        /// </summary>
        public static AudioDiagnosticSettings CreateMinimal()
        {
            return new AudioDiagnosticSettings
            {
                EnableEventLogging = true,
                EnablePerformanceLogging = false,
                IncludeStackTrace = false,
                MaxDiagnosticEntries = 1000
            };
        }
    }
    
    /// <summary>
    /// 音频事件类型
    /// </summary>
    public enum AudioEventType
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error,
        Performance
    }
}