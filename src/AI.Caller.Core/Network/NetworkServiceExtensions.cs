using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Network {
    /// <summary>
    /// 网络监控服务依赖注入扩展方法
    /// </summary>
    public static class NetworkServiceExtensions {
        /// <summary>
        /// 添加网络监控服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddNetworkMonitoring(this IServiceCollection services) {
            services.AddSingleton<INetworkMonitoringService, NetworkMonitoringService>();
            return services;
        }

        /// <summary>
        /// 添加网络监控服务的托管服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddNetworkMonitoringHostedService(this IServiceCollection services) {
            services.AddHostedService<NetworkMonitoringBackgroundService>();
            return services;
        }

        /// <summary>
        /// 验证网络监控服务配置
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <returns>验证结果</returns>
        public static NetworkServiceValidationResult ValidateNetworkServices(this IServiceProvider serviceProvider) {
            var result = new NetworkServiceValidationResult();

            try {
                // 验证网络监控服务
                var networkService = serviceProvider.GetService<INetworkMonitoringService>();
                if (networkService == null) {
                    result.Errors.Add("INetworkMonitoringService not registered");
                }

                // 验证日志服务
                var logger = serviceProvider.GetService<ILogger<NetworkMonitoringService>>();
                if (logger == null) {
                    result.Errors.Add("Logger for NetworkMonitoringService not available");
                }

                result.IsValid = result.Errors.Count == 0;
            } catch (Exception ex) {
                result.Errors.Add($"Network service validation exception: {ex.Message}");
                result.IsValid = false;
            }

            return result;
        }
    }

    /// <summary>
    /// 网络服务验证结果
    /// </summary>
    public class NetworkServiceValidationResult {
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误列表
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// 验证时间
        /// </summary>
        public DateTime ValidationTime { get; set; } = DateTime.UtcNow;

        public override string ToString() {
            return $"Valid: {IsValid}, Errors: {Errors.Count}";
        }
    }
}