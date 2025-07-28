using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Network
{
    /// <summary>
    /// 网络监控后台服务，管理网络监控的生命周期
    /// </summary>
    public class NetworkMonitoringBackgroundService : BackgroundService
    {
        private readonly ILogger<NetworkMonitoringBackgroundService> _logger;
        private readonly INetworkMonitoringService _networkMonitoringService;

        public NetworkMonitoringBackgroundService(
            ILogger<NetworkMonitoringBackgroundService> logger,
            INetworkMonitoringService networkMonitoringService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _networkMonitoringService = networkMonitoringService ?? throw new ArgumentNullException(nameof(networkMonitoringService));
        }

        /// <summary>
        /// 启动网络监控后台服务
        /// </summary>
        /// <param name="stoppingToken">取消令牌</param>
        /// <returns>执行任务</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Network monitoring background service starting...");

            try
            {
                // 订阅网络事件
                SubscribeToNetworkEvents();

                // 启动网络监控
                await _networkMonitoringService.StartMonitoringAsync();

                _logger.LogInformation("Network monitoring background service started successfully");

                // 保持服务运行直到取消
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Network monitoring background service was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Network monitoring background service encountered an error: {Message}", ex.Message);
                throw;
            }
            finally
            {
                await ShutdownAsync();
            }
        }

        /// <summary>
        /// 关闭网络监控服务
        /// </summary>
        /// <returns>关闭任务</returns>
        private async Task ShutdownAsync()
        {
            try
            {
                _logger.LogInformation("Shutting down network monitoring background service...");

                // 取消订阅事件
                UnsubscribeFromNetworkEvents();

                // 停止网络监控
                await _networkMonitoringService.StopMonitoringAsync();

                _logger.LogInformation("Network monitoring background service shutdown completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during network monitoring background service shutdown: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// 订阅网络事件
        /// </summary>
        private void SubscribeToNetworkEvents()
        {
            try
            {
                _networkMonitoringService.NetworkStatusChanged += OnNetworkStatusChanged;
                _networkMonitoringService.NetworkConnectionLost += OnNetworkConnectionLost;
                _networkMonitoringService.NetworkConnectionRestored += OnNetworkConnectionRestored;
                _networkMonitoringService.NetworkQualityChanged += OnNetworkQualityChanged;

                _logger.LogDebug("Subscribed to network monitoring events");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing to network events: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// 取消订阅网络事件
        /// </summary>
        private void UnsubscribeFromNetworkEvents()
        {
            try
            {
                _networkMonitoringService.NetworkStatusChanged -= OnNetworkStatusChanged;
                _networkMonitoringService.NetworkConnectionLost -= OnNetworkConnectionLost;
                _networkMonitoringService.NetworkConnectionRestored -= OnNetworkConnectionRestored;
                _networkMonitoringService.NetworkQualityChanged -= OnNetworkQualityChanged;

                _logger.LogDebug("Unsubscribed from network monitoring events");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from network events: {Message}", ex.Message);
            }
        }

        #region 网络事件处理

        private void OnNetworkStatusChanged(object? sender, NetworkStatusEventArgs e)
        {
            _logger.LogInformation("Network status changed: {Status}", e.CurrentStatus);

            if (e.IsImprovement)
            {
                _logger.LogInformation("Network status improved: {Quality}", e.CurrentStatus.Quality);
            }
            else if (e.IsDegradation)
            {
                _logger.LogWarning("Network status degraded: {Quality}, Issues: {IssueCount}", 
                    e.CurrentStatus.Quality, e.CurrentStatus.Issues.Count);
            }
        }

        private void OnNetworkConnectionLost(object? sender, NetworkConnectionLostEventArgs e)
        {
            _logger.LogError("Network connection lost: {Reason}, Affected clients: {ClientCount}", 
                e.Reason, e.AffectedClientIds.Count);

            // 这里可以添加连接丢失时的处理逻辑
            // 例如：通知其他服务、触发重连机制等
        }

        private void OnNetworkConnectionRestored(object? sender, NetworkConnectionRestoredEventArgs e)
        {
            _logger.LogInformation("Network connection restored after {Duration}, Restored clients: {ClientCount}", 
                e.OutageDuration, e.RestoredClientIds.Count);

            // 这里可以添加连接恢复时的处理逻辑
            // 例如：恢复服务、重新注册客户端等
        }

        private void OnNetworkQualityChanged(object? sender, NetworkQualityChangedEventArgs e)
        {
            var changeType = e.IsImprovement ? "improved" : e.IsDegradation ? "degraded" : "changed";
            var logLevel = e.IsDegradation ? LogLevel.Warning : LogLevel.Information;

            _logger.Log(logLevel, "Network quality {ChangeType} from {PreviousQuality} to {CurrentQuality}", 
                changeType, e.PreviousQuality, e.CurrentQuality);

            // 根据网络质量变化调整系统行为
            if (e.CurrentQuality == NetworkQuality.Poor)
            {
                _logger.LogWarning("Poor network quality detected. Consider reducing bandwidth usage or enabling quality adaptation");
            }
            else if (e.CurrentQuality == NetworkQuality.Excellent && e.PreviousQuality != NetworkQuality.Excellent)
            {
                _logger.LogInformation("Excellent network quality detected. Full quality features can be enabled");
            }
        }

        #endregion

        public override void Dispose()
        {
            try
            {
                UnsubscribeFromNetworkEvents();
                base.Dispose();
                _logger.LogInformation("Network monitoring background service disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing network monitoring background service: {Message}", ex.Message);
            }
        }
    }
}