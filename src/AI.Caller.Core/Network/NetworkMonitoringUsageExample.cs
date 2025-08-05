using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Network {
    /// <summary>
    /// 网络监控服务使用示例
    /// </summary>
    public class NetworkMonitoringUsageExample {
        /// <summary>
        /// 基本网络监控配置示例
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>配置后的服务集合</returns>
        public static IServiceCollection ConfigureBasicNetworkMonitoring(IServiceCollection services) {
            // 添加日志服务
            services.AddLogging(builder => {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // 添加网络监控服务
            services.AddNetworkMonitoring();

            // 添加托管服务
            services.AddNetworkMonitoringHostedService();

            return services;
        }

        /// <summary>
        /// 完整的应用程序配置示例
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>配置后的主机</returns>
        public static async Task<IHost> CreateHostWithNetworkMonitoringAsync(string[] args) {
            var builder = Host.CreateDefaultBuilder(args);

            builder.ConfigureServices((context, services) => {
                // 配置网络监控
                services.AddNetworkMonitoring();
                services.AddNetworkMonitoringHostedService();

                // 添加其他应用程序服务
                // services.AddSingleton<MyApplicationService>();
            });

            var host = builder.Build();

            // 验证配置
            var validationResult = host.Services.ValidateNetworkServices();
            if (!validationResult.IsValid) {
                var logger = host.Services.GetRequiredService<ILogger<NetworkMonitoringUsageExample>>();
                logger.LogError("Network service configuration validation failed: {Errors}",
                    string.Join(", ", validationResult.Errors));
                throw new InvalidOperationException("Network service configuration is invalid");
            }

            return host;
        }

        /// <summary>
        /// 使用网络监控服务的示例
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <returns>使用示例任务</returns>
        public static async Task UseNetworkMonitoringExampleAsync(IServiceProvider serviceProvider) {
            var logger = serviceProvider.GetRequiredService<ILogger<NetworkMonitoringUsageExample>>();
            var networkService = serviceProvider.GetRequiredService<INetworkMonitoringService>();

            try {
                logger.LogInformation("Starting network monitoring usage example...");

                // 订阅网络事件
                networkService.NetworkStatusChanged += (sender, e) => {
                    logger.LogInformation("Network status changed: {Status}", e.CurrentStatus);
                };

                networkService.NetworkConnectionLost += (sender, e) => {
                    logger.LogWarning("Network connection lost: {Reason}", e.Reason);
                };

                networkService.NetworkConnectionRestored += (sender, e) => {
                    logger.LogInformation("Network connection restored after {Duration}", e.OutageDuration);
                };

                // 注册一些模拟的SIP客户端
                await RegisterSampleClientsAsync(networkService, logger);

                // 启动网络监控
                await networkService.StartMonitoringAsync();

                // 获取当前网络状态
                var currentStatus = networkService.GetCurrentNetworkStatus();
                logger.LogInformation("Current network status: {Status}", currentStatus);

                // 获取所有客户端状态
                var clientStatuses = networkService.GetAllClientNetworkStatus();
                logger.LogInformation("Registered clients: {ClientCount}", clientStatuses.Count);

                // 运行一段时间以观察网络监控
                await Task.Delay(TimeSpan.FromMinutes(1));

                // 获取监控统计
                var stats = networkService.GetMonitoringStats();
                logger.LogInformation("Monitoring stats: {Stats}", stats);

                // 停止网络监控
                await networkService.StopMonitoringAsync();

                logger.LogInformation("Network monitoring usage example completed");
            } catch (Exception ex) {
                logger.LogError(ex, "Error in network monitoring usage example: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 注册示例客户端
        /// </summary>
        /// <param name="networkService">网络监控服务</param>
        /// <param name="logger">日志记录器</param>
        /// <returns>注册任务</returns>
        private static async Task RegisterSampleClientsAsync(
            INetworkMonitoringService networkService,
            ILogger logger) {
            logger.LogInformation("Registering sample SIP clients...");

            // 注册几个模拟的SIP客户端
            var sampleClients = new[]
            {
                new { Id = "client-001", Type = "SIPClient", Endpoint = "192.168.1.100:5060" },
                new { Id = "client-002", Type = "SIPClient", Endpoint = "192.168.1.101:5060" },
                new { Id = "client-003", Type = "SIPClient", Endpoint = "10.0.0.50:5060" }
            };

            foreach (var client in sampleClients) {
                // 创建一个模拟的SIP客户端对象
                var mockSipClient = new MockSipClient {
                    Id = client.Id,
                    Type = client.Type,
                    Endpoint = client.Endpoint
                };

                networkService.RegisterSipClient(client.Id, mockSipClient);
                logger.LogInformation("Registered client: {ClientId} ({Endpoint})", client.Id, client.Endpoint);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 监控网络状态变化的示例
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>监控任务</returns>
        public static async Task MonitorNetworkChangesAsync(
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken) {
            var logger = serviceProvider.GetRequiredService<ILogger<NetworkMonitoringUsageExample>>();
            var networkService = serviceProvider.GetRequiredService<INetworkMonitoringService>();

            logger.LogInformation("Starting network change monitoring...");

            try {
                while (!cancellationToken.IsCancellationRequested) {
                    // 手动检查网络状态
                    var networkStatus = await networkService.CheckNetworkStatusAsync();

                    logger.LogInformation("Network check - Connected: {IsConnected}, Quality: {Quality}, " +
                                        "Latency: {Latency}ms, Loss: {PacketLoss}%",
                        networkStatus.IsConnected, networkStatus.Quality,
                        networkStatus.LatencyMs, networkStatus.PacketLossRate);

                    // 检查是否有网络问题
                    if (networkStatus.Issues.Count > 0) {
                        foreach (var issue in networkStatus.Issues) {
                            logger.LogWarning("Network issue detected: {Issue}", issue);
                        }
                    }

                    // 获取客户端状态
                    var clientStatuses = networkService.GetAllClientNetworkStatus();
                    var onlineClients = clientStatuses.Values.Count(c => c.IsOnline);

                    if (clientStatuses.Count > 0) {
                        logger.LogInformation("Client status - Online: {OnlineCount}/{TotalCount}",
                            onlineClients, clientStatuses.Count);
                    }

                    // 等待30秒再次检查
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            } catch (OperationCanceledException) {
                logger.LogInformation("Network change monitoring cancelled");
            } catch (Exception ex) {
                logger.LogError(ex, "Error during network change monitoring: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// 网络质量自适应示例
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <returns>自适应任务</returns>
        public static async Task NetworkQualityAdaptationExampleAsync(IServiceProvider serviceProvider) {
            var logger = serviceProvider.GetRequiredService<ILogger<NetworkMonitoringUsageExample>>();
            var networkService = serviceProvider.GetRequiredService<INetworkMonitoringService>();

            logger.LogInformation("Starting network quality adaptation example...");

            // 订阅网络质量变化事件
            networkService.NetworkQualityChanged += (sender, e) => {
                logger.LogInformation("Adapting to network quality change: {PreviousQuality} -> {CurrentQuality}",
                    e.PreviousQuality, e.CurrentQuality);

                // 根据网络质量调整应用行为
                switch (e.CurrentQuality) {
                    case NetworkQuality.Excellent:
                        logger.LogInformation("Enabling high-quality features (HD audio, video, etc.)");
                        break;

                    case NetworkQuality.Good:
                        logger.LogInformation("Using standard quality settings");
                        break;

                    case NetworkQuality.Fair:
                        logger.LogInformation("Reducing quality to maintain stability");
                        break;

                    case NetworkQuality.Poor:
                        logger.LogWarning("Switching to low-quality mode to preserve connectivity");
                        break;

                    case NetworkQuality.Disconnected:
                        logger.LogError("Network disconnected - switching to offline mode");
                        break;
                }
            };

            // 模拟运行一段时间
            await Task.Delay(TimeSpan.FromMinutes(2));

            logger.LogInformation("Network quality adaptation example completed");
        }

        /// <summary>
        /// 模拟SIP客户端类
        /// </summary>
        private class MockSipClient {
            public string Id { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Endpoint { get; set; } = string.Empty;

            public override string ToString() {
                return $"{Type} {Id} ({Endpoint})";
            }
        }
    }
}