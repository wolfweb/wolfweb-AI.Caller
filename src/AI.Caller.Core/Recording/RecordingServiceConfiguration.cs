using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 录音服务配置管理器
    /// </summary>
    public static class RecordingServiceConfiguration
    {
        /// <summary>
        /// 从配置文件加载录音选项
        /// </summary>
        /// <param name="configuration">配置对象</param>
        /// <param name="sectionName">配置节名称</param>
        /// <returns>录音选项</returns>
        public static RecordingOptions LoadFromConfiguration(IConfiguration configuration, string sectionName = "Recording")
        {
            var recordingOptions = new RecordingOptions();
            configuration.GetSection(sectionName).Bind(recordingOptions);

            // 验证配置
            var validation = recordingOptions.Validate();
            if (!validation.IsValid)
            {
                throw new InvalidOperationException($"Invalid recording configuration: {string.Join(", ", validation.Errors)}");
            }

            return recordingOptions;
        }

        /// <summary>
        /// 配置录音服务的日志记录
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureLogging">日志配置委托</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection ConfigureRecordingLogging(
            this IServiceCollection services,
            Action<ILoggingBuilder>? configureLogging = null)
        {
            if (configureLogging != null)
            {
                services.AddLogging(configureLogging);
            }
            else
            {
                // 默认日志配置
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.AddDebug();
                    builder.SetMinimumLevel(LogLevel.Information);

                    // 为录音相关组件设置特定的日志级别
                    builder.AddFilter("AI.Caller.Core.Recording", LogLevel.Debug);
                });
            }

            return services;
        }

        /// <summary>
        /// 创建录音服务的完整配置
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configuration">配置对象</param>
        /// <param name="configureOptions">选项配置委托</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddCompleteRecordingConfiguration(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<RecordingOptions>? configureOptions = null)
        {
            // 加载基础配置
            var recordingOptions = LoadFromConfiguration(configuration);

            // 应用额外配置
            configureOptions?.Invoke(recordingOptions);

            // 添加录音服务
            services.AddRecordingServices(recordingOptions);

            // 添加托管服务
            services.AddRecordingHostedServices();

            // 配置日志
            services.ConfigureRecordingLogging();

            return services;
        }

        /// <summary>
        /// 验证录音服务的运行时配置
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <returns>配置验证结果</returns>
        public static async Task<RecordingConfigurationValidationResult> ValidateRuntimeConfigurationAsync(
            IServiceProvider serviceProvider)
        {
            var result = new RecordingConfigurationValidationResult();

            try
            {
                // 验证服务注册
                var serviceValidation = serviceProvider.ValidateRecordingServices();
                if (!serviceValidation.IsValid)
                {
                    result.Errors.AddRange(serviceValidation.Errors);
                }

                // 验证文件系统权限
                await ValidateFileSystemPermissionsAsync(serviceProvider, result);

                // 验证音频编码器可用性
                await ValidateAudioEncodersAsync(serviceProvider, result);

                // 验证网络连接（如果需要）
                await ValidateNetworkConnectivityAsync(serviceProvider, result);

                result.IsValid = result.Errors.Count == 0;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Runtime validation exception: {ex.Message}");
                result.IsValid = false;
            }

            return result;
        }

        /// <summary>
        /// 验证文件系统权限
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <param name="result">验证结果</param>
        /// <returns>验证任务</returns>
        private static async Task ValidateFileSystemPermissionsAsync(
            IServiceProvider serviceProvider, 
            RecordingConfigurationValidationResult result)
        {
            try
            {
                var recordingOptions = serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<RecordingOptions>>();
                if (recordingOptions?.Value != null)
                {
                    var outputDirectory = recordingOptions.Value.OutputDirectory;

                    // 检查目录是否存在，如果不存在尝试创建
                    if (!Directory.Exists(outputDirectory))
                    {
                        try
                        {
                            Directory.CreateDirectory(outputDirectory);
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"Cannot create output directory '{outputDirectory}': {ex.Message}");
                            return;
                        }
                    }

                    // 测试写入权限
                    var testFile = Path.Combine(outputDirectory, $"test_{Guid.NewGuid():N}.tmp");
                    try
                    {
                        await File.WriteAllTextAsync(testFile, "test");
                        File.Delete(testFile);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"No write permission for output directory '{outputDirectory}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"File system validation error: {ex.Message}");
            }
        }

        /// <summary>
        /// 验证音频编码器可用性
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <param name="result">验证结果</param>
        /// <returns>验证任务</returns>
        private static async Task ValidateAudioEncodersAsync(
            IServiceProvider serviceProvider,
            RecordingConfigurationValidationResult result)
        {
            try
            {
                var streamingEncoder = serviceProvider.GetService<IStreamingAudioEncoder>();
                if (streamingEncoder == null)
                {
                    result.Errors.Add("IStreamingAudioEncoder service not registered");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Audio encoder validation error: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 验证网络连接
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <param name="result">验证结果</param>
        /// <returns>验证任务</returns>
        private static async Task ValidateNetworkConnectivityAsync(
            IServiceProvider serviceProvider,
            RecordingConfigurationValidationResult result)
        {
            // 这里可以添加网络连接验证逻辑
            // 例如检查是否能够连接到外部服务或数据库
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// 录音配置验证结果
    /// </summary>
    public class RecordingConfigurationValidationResult
    {
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误列表
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// 警告列表
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// 验证时间
        /// </summary>
        public DateTime ValidationTime { get; set; } = DateTime.UtcNow;

        public override string ToString()
        {
            return $"Valid: {IsValid}, Errors: {Errors.Count}, Warnings: {Warnings.Count}";
        }
    }
}