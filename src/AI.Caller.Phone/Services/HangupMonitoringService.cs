using AI.Caller.Phone.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace AI.Caller.Phone.Services {
    public class HangupMonitoringService {
        private readonly ILogger<HangupMonitoringService> _logger;
        private readonly ConcurrentDictionary<string, HangupMetrics> _activeHangups;
        private readonly ConcurrentQueue<HangupAuditLog> _auditLogs;
        private readonly Timer _metricsTimer;
        private readonly object _metricsLock = new object();

        private int _totalHangupAttempts = 0;
        private int _successfulHangups = 0;
        private int _failedHangups = 0;
        private int _timeoutHangups = 0;
        private readonly List<double> _hangupResponseTimes = new List<double>();

        public HangupMonitoringService(ILogger<HangupMonitoringService> logger) {
            _logger = logger;
            _activeHangups = new ConcurrentDictionary<string, HangupMetrics>();
            _auditLogs = new ConcurrentQueue<HangupAuditLog>();

            _metricsTimer = new Timer(LogMetrics, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public string StartHangupMonitoring(string sipUsername, string reason) {
            var hangupId = Guid.NewGuid().ToString();
            var metrics = new HangupMetrics {
                HangupId = hangupId,
                SipUsername = sipUsername,
                Reason = reason,
                StartTime = DateTime.UtcNow,
                Stopwatch = Stopwatch.StartNew()
            };

            _activeHangups.TryAdd(hangupId, metrics);

            Interlocked.Increment(ref _totalHangupAttempts);

            _logger.LogInformation($"开始监控挂断操作 - ID: {hangupId}, 用户: {sipUsername}, 原因: {reason}");

            var auditLog = new HangupAuditLog {
                HangupId = hangupId,
                SipUsername = sipUsername,
                Action = "HangupStarted",
                Timestamp = DateTime.UtcNow,
                Details = $"Reason: {reason}"
            };
            _auditLogs.Enqueue(auditLog);

            return hangupId;
        }

        public void LogHangupStep(string hangupId, string step, string details = "") {
            if (_activeHangups.TryGetValue(hangupId, out var metrics)) {
                var elapsed = metrics.Stopwatch.ElapsedMilliseconds;
                _logger.LogInformation($"挂断步骤 - ID: {hangupId}, 步骤: {step}, 耗时: {elapsed}ms, 详情: {details}");

                var auditLog = new HangupAuditLog {
                    HangupId = hangupId,
                    SipUsername = metrics.SipUsername ?? "",
                    Action = step,
                    Timestamp = DateTime.UtcNow,
                    Details = $"{details} (Elapsed: {elapsed}ms)"
                };
                _auditLogs.Enqueue(auditLog);

                if (elapsed > 30000) // 30秒
                {
                    _logger.LogWarning($"挂断操作可能存在资源泄漏 - ID: {hangupId}, 用户: {metrics.SipUsername}, 已耗时: {elapsed}ms");
                }
            }
        }

        public void CompleteHangupMonitoring(string hangupId, bool success, string? errorMessage = null) {
            if (_activeHangups.TryRemove(hangupId, out var metrics)) {
                metrics.Stopwatch.Stop();
                var totalTime = metrics.Stopwatch.ElapsedMilliseconds;

                lock (_metricsLock) {
                    _hangupResponseTimes.Add(totalTime);

                    if (_hangupResponseTimes.Count > 1000) {
                        _hangupResponseTimes.RemoveAt(0);
                    }
                }

                if (success) {
                    Interlocked.Increment(ref _successfulHangups);
                    _logger.LogInformation($"挂断操作成功完成 - ID: {hangupId}, 用户: {metrics.SipUsername}, 耗时: {totalTime}ms");
                } else {
                    Interlocked.Increment(ref _failedHangups);
                    _logger.LogError($"挂断操作失败 - ID: {hangupId}, 用户: {metrics.SipUsername}, 耗时: {totalTime}ms, 错误: {errorMessage}");
                }

                var auditLog = new HangupAuditLog {
                    HangupId = hangupId,
                    SipUsername = metrics.SipUsername,
                    Action = success ? "HangupCompleted" : "HangupFailed",
                    Timestamp = DateTime.UtcNow,
                    Details = success ? $"Total time: {totalTime}ms" : $"Error: {errorMessage}, Total time: {totalTime}ms"
                };
                _auditLogs.Enqueue(auditLog);

                if (totalTime > 10000) // 10秒超时
                {
                    Interlocked.Increment(ref _timeoutHangups);
                    _logger.LogWarning($"挂断操作超时 - ID: {hangupId}, 用户: {metrics.SipUsername}, 耗时: {totalTime}ms");
                }
            }
        }

        public double GetHangupSuccessRate() {
            var total = _totalHangupAttempts;
            if (total == 0) return 100.0;

            return (_successfulHangups * 100.0) / total;
        }

        public double GetAverageResponseTime() {
            lock (_metricsLock) {
                if (_hangupResponseTimes.Count == 0) return 0.0;
                return _hangupResponseTimes.Average();
            }
        }

        public int GetActiveHangupCount() {
            return _activeHangups.Count;
        }

        public List<string> DetectResourceLeaks() {
            var leaks = new List<string>();
            var now = DateTime.UtcNow;

            foreach (var kvp in _activeHangups) {
                var metrics = kvp.Value;
                var elapsed = now - metrics.StartTime;

                if (elapsed.TotalMinutes > 5) // 5分钟还没完成的操作
                {
                    leaks.Add($"挂断操作可能泄漏 - ID: {metrics.HangupId}, 用户: {metrics.SipUsername}, 已运行: {elapsed.TotalMinutes:F1}分钟");
                }
            }

            return leaks;
        }

        public List<HangupAuditLog> GetAuditLogs(int maxCount = 100) {
            var logs = new List<HangupAuditLog>();
            var count = 0;

            while (_auditLogs.TryDequeue(out var log) && count < maxCount) {
                logs.Add(log);
                count++;
            }

            return logs.OrderByDescending(x => x.Timestamp).ToList();
        }

        private void LogMetrics(object? state) {
            try {
                var successRate = GetHangupSuccessRate();
                var avgResponseTime = GetAverageResponseTime();
                var activeCount = GetActiveHangupCount();
                var leaks = DetectResourceLeaks();

                _logger.LogInformation($"挂断操作监控指标 - " +
                    $"总尝试: {_totalHangupAttempts}, " +
                    $"成功: {_successfulHangups}, " +
                    $"失败: {_failedHangups}, " +
                    $"超时: {_timeoutHangups}, " +
                    $"成功率: {successRate:F1}%, " +
                    $"平均响应时间: {avgResponseTime:F0}ms, " +
                    $"活动操作: {activeCount}");

                foreach (var leak in leaks) {
                    _logger.LogWarning($"资源泄漏检测: {leak}");
                }

                CleanupAuditLogs();
            } catch (Exception ex) {
                _logger.LogError(ex, "输出监控指标时发生错误");
            }
        }

        private void CleanupAuditLogs() {
            var cutoffTime = DateTime.UtcNow.AddHours(-1);
            var tempLogs = new List<HangupAuditLog>();

            while (_auditLogs.TryDequeue(out var log)) {
                if (log.Timestamp > cutoffTime) {
                    tempLogs.Add(log);
                }
            }

            foreach (var log in tempLogs) {
                _auditLogs.Enqueue(log);
            }
        }

        public void Dispose() {
            _metricsTimer?.Dispose();
        }
    }

    public class HangupMetrics {
        public string HangupId { get; set; } = string.Empty;
        public string SipUsername { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public Stopwatch Stopwatch { get; set; } = new Stopwatch();
    }

    public class HangupAuditLog {
        public string HangupId { get; set; } = string.Empty;
        public string SipUsername { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Details { get; set; } = string.Empty;
    }
}