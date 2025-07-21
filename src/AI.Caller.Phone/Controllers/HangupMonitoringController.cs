using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.Caller.Phone.Controllers
{
    /// <summary>
    /// 挂断操作监控API控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // 需要授权访问监控数据
    public class HangupMonitoringController : ControllerBase
    {
        private readonly HangupMonitoringService _monitoringService;
        private readonly ILogger<HangupMonitoringController> _logger;

        public HangupMonitoringController(
            HangupMonitoringService monitoringService,
            ILogger<HangupMonitoringController> logger)
        {
            _monitoringService = monitoringService;
            _logger = logger;
        }

        /// <summary>
        /// 获取挂断操作监控指标
        /// </summary>
        [HttpGet("metrics")]
        public ActionResult<object> GetMetrics()
        {
            try
            {
                var metrics = new
                {
                    SuccessRate = _monitoringService.GetHangupSuccessRate(),
                    AverageResponseTime = _monitoringService.GetAverageResponseTime(),
                    ActiveHangupCount = _monitoringService.GetActiveHangupCount(),
                    Timestamp = DateTime.UtcNow
                };

                return Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取监控指标时发生错误");
                return StatusCode(500, new { error = "获取监控指标失败" });
            }
        }

        /// <summary>
        /// 检测资源泄漏
        /// </summary>
        [HttpGet("resource-leaks")]
        public ActionResult<object> GetResourceLeaks()
        {
            try
            {
                var leaks = _monitoringService.DetectResourceLeaks();
                
                return Ok(new
                {
                    LeakCount = leaks.Count,
                    Leaks = leaks,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检测资源泄漏时发生错误");
                return StatusCode(500, new { error = "检测资源泄漏失败" });
            }
        }

        /// <summary>
        /// 获取审计日志
        /// </summary>
        [HttpGet("audit-logs")]
        public ActionResult<object> GetAuditLogs([FromQuery] int maxCount = 50)
        {
            try
            {
                if (maxCount <= 0 || maxCount > 1000)
                {
                    maxCount = 50;
                }

                var logs = _monitoringService.GetAuditLogs(maxCount);
                
                return Ok(new
                {
                    LogCount = logs.Count,
                    Logs = logs,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取审计日志时发生错误");
                return StatusCode(500, new { error = "获取审计日志失败" });
            }
        }

        /// <summary>
        /// 获取健康状态
        /// </summary>
        [HttpGet("health")]
        public ActionResult<object> GetHealth()
        {
            try
            {
                var successRate = _monitoringService.GetHangupSuccessRate();
                var avgResponseTime = _monitoringService.GetAverageResponseTime();
                var activeCount = _monitoringService.GetActiveHangupCount();
                var leakCount = _monitoringService.DetectResourceLeaks().Count;

                var health = "Healthy";
                var issues = new List<string>();

                // 健康检查规则
                if (successRate < 95.0)
                {
                    health = "Warning";
                    issues.Add($"挂断成功率过低: {successRate:F1}%");
                }

                if (avgResponseTime > 5000) // 5秒
                {
                    health = "Warning";
                    issues.Add($"平均响应时间过长: {avgResponseTime:F0}ms");
                }

                if (activeCount > 10)
                {
                    health = "Warning";
                    issues.Add($"活动挂断操作过多: {activeCount}");
                }

                if (leakCount > 0)
                {
                    health = "Critical";
                    issues.Add($"检测到资源泄漏: {leakCount}个");
                }

                return Ok(new
                {
                    Status = health,
                    Issues = issues,
                    Metrics = new
                    {
                        SuccessRate = successRate,
                        AverageResponseTime = avgResponseTime,
                        ActiveCount = activeCount,
                        LeakCount = leakCount
                    },
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取健康状态时发生错误");
                return StatusCode(500, new { error = "获取健康状态失败" });
            }
        }
    }
}