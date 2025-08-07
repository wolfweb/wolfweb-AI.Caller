using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.BackgroundTask {
    public class UserSessionCleanupService : BackgroundService {
        private readonly ILogger<UserSessionCleanupService> _logger;
        private readonly ApplicationContext _applicationContext;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5); // 每5分钟检查一次
        private readonly TimeSpan _userInactivityTimeout = TimeSpan.FromHours(2); // 2小时无活动则清理

        public UserSessionCleanupService(
            ILogger<UserSessionCleanupService> logger,
            ApplicationContext applicationContext) {
            _logger = logger;
            _applicationContext = applicationContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            _logger.LogInformation("用户会话清理服务已启动");

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await CleanupInactiveUsersAsync();
                    await Task.Delay(_cleanupInterval, stoppingToken);
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    _logger.LogError(ex, "用户会话清理过程中发生错误");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("用户会话清理服务已停止");
        }

        private async Task CleanupInactiveUsersAsync() {
            try {
                var inactiveUsers = _applicationContext.GetInactiveUsers(_userInactivityTimeout);

                if (inactiveUsers.Count == 0) {
                    _logger.LogDebug("没有发现需要清理的非活跃用户");
                    return;
                }

                _logger.LogInformation($"发现 {inactiveUsers.Count} 个非活跃用户需要清理");

                var cleanedCount = 0;
                foreach (var sipUsername in inactiveUsers) {
                    try {
                        if (_applicationContext.RemoveSipClient(sipUsername)) {
                            cleanedCount++;
                            _logger.LogInformation($"已清理非活跃用户: {sipUsername}");
                        }
                    } catch (Exception ex) {
                        _logger.LogError(ex, $"清理用户 {sipUsername} 时发生错误");
                    }
                }

                if (cleanedCount > 0) {
                    _logger.LogInformation($"用户会话清理完成，共清理 {cleanedCount} 个用户");

                    // 记录当前活跃用户数量
                    var activeUserCount = _applicationContext.SipClients.Count;
                    var totalSessionCount = _applicationContext.UserSessions.Count;
                    _logger.LogInformation($"当前活跃用户: {activeUserCount}, 总会话数: {totalSessionCount}");
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "清理非活跃用户时发生错误");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("正在停止用户会话清理服务...");
            await base.StopAsync(cancellationToken);
        }
    }
}