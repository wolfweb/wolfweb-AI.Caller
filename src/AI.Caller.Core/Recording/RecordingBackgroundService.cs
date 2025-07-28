using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 录音系统后台服务，管理录音组件的生命周期
    /// </summary>
    public class RecordingBackgroundService : BackgroundService
    {
        private readonly ILogger<RecordingBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly RecordingOptions _recordingOptions;
        private AudioDataFlowMonitor? _dataFlowMonitor;
        private IRecordingStatusService? _statusService;
        private bool _isInitialized = false;

        public RecordingBackgroundService(
            ILogger<RecordingBackgroundService> logger,
            IServiceProvider serviceProvider,
            IOptions<RecordingOptions> recordingOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _recordingOptions = recordingOptions?.Value ?? throw new ArgumentNullException(nameof(recordingOptions));
        }

        /// <summary>
        /// 启动录音系统服务
        /// </summary>
        /// <param name="stoppingToken">取消令牌</param>
        /// <returns>执行任务</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Recording background service starting...");

            try
            {
                // 初始化录音系统
                await InitializeRecordingSystemAsync(stoppingToken);

                // 主循环 - 监控录音系统状态
                await MonitorRecordingSystemAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Recording background service was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recording background service encountered an error: {Message}", ex.Message);
                throw;
            }
            finally
            {
                await ShutdownRecordingSystemAsync();
                _logger.LogInformation("Recording background service stopped");
            }
        }

        /// <summary>
        /// 初始化录音系统
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>初始化任务</returns>
        private async Task InitializeRecordingSystemAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing recording system...");

            try
            {
                // 验证服务配置
                var validationResult = _serviceProvider.ValidateRecordingServices();
                if (!validationResult.IsValid)
                {
                    var errors = string.Join(", ", validationResult.Errors);
                    throw new InvalidOperationException($"Recording service validation failed: {errors}");
                }

                _logger.LogInformation("Recording service validation passed");

                // 获取核心服务
                _dataFlowMonitor = _serviceProvider.GetRequiredService<AudioDataFlowMonitor>();
                _statusService = _serviceProvider.GetRequiredService<IRecordingStatusService>();

                // 注册监控器到状态服务
                if (_statusService is RecordingStatusService statusServiceImpl)
                {
                    statusServiceImpl.RegisterDataFlowMonitor(_dataFlowMonitor);
                }

                // 启动数据流监控
                _dataFlowMonitor.StartMonitoring();
                _logger.LogInformation("Audio data flow monitoring started");

                // 创建输出目录
                if (!Directory.Exists(_recordingOptions.OutputDirectory))
                {
                    Directory.CreateDirectory(_recordingOptions.OutputDirectory);
                    _logger.LogInformation("Created recording output directory: {Directory}", _recordingOptions.OutputDirectory);
                }

                // 订阅系统事件
                SubscribeToSystemEvents();

                _isInitialized = true;
                _logger.LogInformation("Recording system initialized successfully");

                // 等待一小段时间确保所有组件都已启动
                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize recording system: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 监控录音系统状态
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>监控任务</returns>
        private async Task MonitorRecordingSystemAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting recording system monitoring...");

            var healthCheckInterval = TimeSpan.FromSeconds(30);
            var lastHealthCheck = DateTime.MinValue;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 定期健康检查
                    if (DateTime.UtcNow - lastHealthCheck >= healthCheckInterval)
                    {
                        await PerformHealthCheckAsync();
                        lastHealthCheck = DateTime.UtcNow;
                    }

                    // 等待一段时间再进行下次检查
                    await Task.Delay(5000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during recording system monitoring: {Message}", ex.Message);
                    await Task.Delay(10000, cancellationToken); // 出错时等待更长时间
                }
            }
        }

        /// <summary>
        /// 执行健康检查
        /// </summary>
        /// <returns>健康检查任务</returns>
        private async Task PerformHealthCheckAsync()
        {
            if (!_isInitialized || _statusService == null)
                return;

            try
            {
                // 获取系统状态
                var systemStatus = _statusService.GetSystemStatus();
                var healthStatus = _statusService.GetHealthStatus();
                var dataFlowStatus = _statusService.GetDataFlowStatus();

                // 记录健康状态
                _logger.LogDebug("Health check - System: {SystemHealthy}, Health: {HealthHealthy}, DataFlow: {DataFlowHealthy}",
                    systemStatus.IsHealthy, healthStatus.IsHealthy, dataFlowStatus.IsHealthy);

                // 检查是否有问题需要报告
                var allIssues = new List<string>();
                allIssues.AddRange(systemStatus.Issues.Select(i => i.Description));
                allIssues.AddRange(dataFlowStatus.CurrentIssues);

                if (allIssues.Count > 0)
                {
                    _logger.LogWarning("Recording system health issues detected: {Issues}", string.Join(", ", allIssues));
                }

                // 检查磁盘空间
                await CheckDiskSpaceAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during health check: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// 检查磁盘空间
        /// </summary>
        /// <returns>检查任务</returns>
        private async Task CheckDiskSpaceAsync()
        {
            try
            {
                var outputDirectory = new DirectoryInfo(_recordingOptions.OutputDirectory);
                if (outputDirectory.Exists)
                {
                    var drive = new DriveInfo(outputDirectory.Root.FullName);
                    var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    var totalSpaceGB = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);

                    _logger.LogDebug("Disk space check - Free: {FreeSpace:F1}GB, Total: {TotalSpace:F1}GB", 
                        freeSpaceGB, totalSpaceGB);

                    // 警告磁盘空间不足
                    if (freeSpaceGB < 1.0) // 少于1GB
                    {
                        _logger.LogWarning("Low disk space warning: {FreeSpace:F1}GB remaining on {Drive}", 
                            freeSpaceGB, drive.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking disk space: {Message}", ex.Message);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 订阅系统事件
        /// </summary>
        private void SubscribeToSystemEvents()
        {
            if (_statusService == null)
                return;

            try
            {
                _statusService.SystemStatusChanged += OnSystemStatusChanged;
                _statusService.HealthWarning += OnHealthWarning;

                if (_dataFlowMonitor != null)
                {
                    _dataFlowMonitor.HealthStatusChanged += OnDataFlowHealthChanged;
                    _dataFlowMonitor.DataFlowInterrupted += OnDataFlowInterrupted;
                }

                _logger.LogDebug("Subscribed to recording system events");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing to system events: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// 取消订阅系统事件
        /// </summary>
        private void UnsubscribeFromSystemEvents()
        {
            try
            {
                if (_statusService != null)
                {
                    _statusService.SystemStatusChanged -= OnSystemStatusChanged;
                    _statusService.HealthWarning -= OnHealthWarning;
                }

                if (_dataFlowMonitor != null)
                {
                    _dataFlowMonitor.HealthStatusChanged -= OnDataFlowHealthChanged;
                    _dataFlowMonitor.DataFlowInterrupted -= OnDataFlowInterrupted;
                }

                _logger.LogDebug("Unsubscribed from recording system events");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from system events: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// 关闭录音系统
        /// </summary>
        /// <returns>关闭任务</returns>
        private async Task ShutdownRecordingSystemAsync()
        {
            _logger.LogInformation("Shutting down recording system...");

            try
            {
                // 取消订阅事件
                UnsubscribeFromSystemEvents();

                // 停止数据流监控
                if (_dataFlowMonitor != null)
                {
                    _dataFlowMonitor.StopMonitoring();
                    _logger.LogInformation("Audio data flow monitoring stopped");
                }

                _isInitialized = false;
                _logger.LogInformation("Recording system shutdown completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during recording system shutdown: {Message}", ex.Message);
            }

            await Task.CompletedTask;
        }

        #region 事件处理

        private void OnSystemStatusChanged(object? sender, SystemStatusChangedEventArgs e)
        {
            _logger.LogInformation("System status changed: {CurrentStatus} (was {PreviousStatus})",
                e.CurrentStatus.IsHealthy ? "Healthy" : "Unhealthy",
                e.PreviousStatus.IsHealthy ? "Healthy" : "Unhealthy");

            if (e.HealthDegraded)
            {
                _logger.LogWarning("System health degraded: {IssueCount} issues detected", e.CurrentStatus.Issues.Count);
            }
            else if (e.HealthImproved)
            {
                _logger.LogInformation("System health improved");
            }
        }

        private void OnHealthWarning(object? sender, HealthWarningEventArgs e)
        {
            _logger.LogWarning("Health warning: {Issue}", e.Issue);
        }

        private void OnDataFlowHealthChanged(object? sender, RecordingHealthStatus e)
        {
            _logger.LogInformation("Data flow health changed: {Quality} (Healthy: {IsHealthy})", e.Quality, e.IsHealthy);
        }

        private void OnDataFlowInterrupted(object? sender, DataFlowInterruptionEventArgs e)
        {
            _logger.LogWarning("Data flow interrupted for {Duration:F1} seconds", e.InterruptionDuration.TotalSeconds);
        }

        #endregion
    }
}