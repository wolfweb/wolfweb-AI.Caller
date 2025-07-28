using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 录音服务使用示例
    /// </summary>
    public class RecordingServiceUsageExample
    {
        /// <summary>
        /// 基本配置示例
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>配置后的服务集合</returns>
        public static IServiceCollection ConfigureBasicRecordingServices(IServiceCollection services)
        {
            // 基本配置
            services.AddRecordingServices(options =>
            {
                options.Codec = AudioCodec.PCM_WAV;
                options.SampleRate = 8000;
                options.Channels = 1;
                options.OutputDirectory = "./recordings";
                options.FileNameTemplate = "{timestamp}_{caller}_{duration}";
                options.MaxDuration = TimeSpan.FromHours(2);
                options.MaxFileSize = 100 * 1024 * 1024; // 100MB
            });

            // 添加托管服务
            services.AddRecordingHostedServices();

            return services;
        }

        /// <summary>
        /// 从配置文件配置示例
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configuration">配置对象</param>
        /// <returns>配置后的服务集合</returns>
        public static IServiceCollection ConfigureRecordingServicesFromConfig(
            IServiceCollection services, 
            IConfiguration configuration)
        {
            // 从配置文件加载
            services.AddCompleteRecordingConfiguration(configuration, options =>
            {
                // 可以在这里覆盖配置文件中的设置
                options.AutoStart = true;
            });

            return services;
        }

        /// <summary>
        /// 高级配置示例
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>配置后的服务集合</returns>
        public static IServiceCollection ConfigureAdvancedRecordingServices(IServiceCollection services)
        {
            // 高级配置
            services.AddRecordingServices(options =>
            {
                options.Codec = AudioCodec.PCM_WAV;
                options.SampleRate = 16000; // 高质量采样率
                options.Channels = 2; // 立体声
                options.OutputDirectory = "./high-quality-recordings";
                options.Quality = AudioQuality.High;
                options.EnableNoiseReduction = true;
                options.EnableVolumeNormalization = true;
                options.MaxDuration = TimeSpan.FromHours(4);
                options.MaxFileSize = 500 * 1024 * 1024; // 500MB
            });

            // 配置日志
            services.ConfigureRecordingLogging(builder =>
            {
                builder.AddConsole();
                // builder.AddFile("logs/recording-{Date}.txt"); // 需要添加文件日志提供程序包
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddFilter("AI.Caller.Core.Recording", LogLevel.Trace);
            });

            // 添加托管服务
            services.AddRecordingHostedServices();

            return services;
        }

        /// <summary>
        /// 完整的应用程序配置示例
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>配置后的主机</returns>
        public static async Task<IHost> CreateHostWithRecordingServicesAsync(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args);

            builder.ConfigureServices((context, services) =>
            {
                // 从配置文件加载录音服务
                services.AddCompleteRecordingConfiguration(context.Configuration);

                // 添加其他应用程序服务
                // services.AddSingleton<MyApplicationService>();
            });

            var host = builder.Build();

            // 验证配置
            var validationResult = await RecordingServiceConfiguration.ValidateRuntimeConfigurationAsync(host.Services);
            if (!validationResult.IsValid)
            {
                var logger = host.Services.GetRequiredService<ILogger<RecordingServiceUsageExample>>();
                logger.LogError("Recording service configuration validation failed: {Errors}", 
                    string.Join(", ", validationResult.Errors));
                throw new InvalidOperationException("Recording service configuration is invalid");
            }

            return host;
        }

        /// <summary>
        /// 使用录音服务的示例
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <returns>使用示例任务</returns>
        public static async Task UseRecordingServicesExampleAsync(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<RecordingServiceUsageExample>>();
            
            try
            {
                // 获取录音管理器
                var recordingManager = serviceProvider.GetRequiredService<IAudioRecordingManager>();
                
                // 获取音频桥接器
                var audioBridge = serviceProvider.GetRequiredService<IAudioBridge>();
                
                // 获取状态服务
                var statusService = serviceProvider.GetRequiredService<IRecordingStatusService>();

                logger.LogInformation("Recording services obtained successfully");

                // 注册录音管理器到音频桥接器
                audioBridge.RegisterRecordingManager(recordingManager);

                // 创建录音选项
                var recordingOptions = new RecordingOptions
                {
                    Codec = AudioCodec.PCM_WAV,
                    SampleRate = 8000,
                    Channels = 1,
                    OutputDirectory = "./example-recordings",
                    FileNameTemplate = "example_{timestamp}.wav"
                };

                // 开始录音
                var recordingId = await recordingManager.StartRecordingAsync(recordingOptions);
                logger.LogInformation("Recording started with ID: {RecordingId}", recordingId);

                // 模拟一些音频数据
                await SimulateAudioDataAsync(audioBridge, logger);

                // 检查系统状态
                var systemStatus = statusService.GetSystemStatus();
                var healthStatus = statusService.GetHealthStatus();
                
                logger.LogInformation("System Status: {IsHealthy}, Health Status: {IsDataFlowing}", 
                    systemStatus.IsHealthy, healthStatus.IsDataFlowing);

                // 停止录音
                await recordingManager.StopRecordingAsync();
                logger.LogInformation("Recording stopped: {RecordingId}", recordingId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error using recording services: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 模拟音频数据
        /// </summary>
        /// <param name="audioBridge">音频桥接器</param>
        /// <param name="logger">日志记录器</param>
        /// <returns>模拟任务</returns>
        private static async Task SimulateAudioDataAsync(IAudioBridge audioBridge, ILogger logger)
        {
            logger.LogInformation("Simulating audio data...");

            var random = new Random();
            for (int i = 0; i < 100; i++) // 模拟100帧音频数据
            {
                var audioData = new byte[160]; // 20ms @ 8kHz
                random.NextBytes(audioData);

                var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
                audioBridge.ForwardAudioData(AudioSource.RTP_Incoming, audioData, audioFormat);
                await Task.Delay(20); // 20ms间隔
            }

            logger.LogInformation("Audio data simulation completed");
        }

        /// <summary>
        /// 监控录音系统状态的示例
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>监控任务</returns>
        public static async Task MonitorRecordingSystemAsync(
            IServiceProvider serviceProvider, 
            CancellationToken cancellationToken)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<RecordingServiceUsageExample>>();
            var statusService = serviceProvider.GetRequiredService<IRecordingStatusService>();

            logger.LogInformation("Starting recording system monitoring...");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // 获取系统状态
                    var systemStatus = statusService.GetSystemStatus();
                    var healthStatus = statusService.GetHealthStatus();
                    var dataFlowStatus = statusService.GetDataFlowStatus();

                    // 记录状态信息
                    logger.LogInformation("Recording System Status - System: {SystemHealthy}, Health: {HealthHealthy}, DataFlow: {DataFlowHealthy}",
                        systemStatus.IsHealthy, healthStatus.IsHealthy, dataFlowStatus.IsHealthy);

                    // 如果有问题，记录详细信息
                    if (!systemStatus.IsHealthy)
                    {
                        var issues = string.Join(", ", systemStatus.Issues.Select(i => i.Description));
                        logger.LogWarning("System issues detected: {Issues}", issues);
                    }

                    if (dataFlowStatus.CurrentIssues.Count > 0)
                    {
                        var issues = string.Join(", ", dataFlowStatus.CurrentIssues);
                        logger.LogWarning("Data flow issues detected: {Issues}", issues);
                    }

                    // 等待30秒再次检查
                    await Task.Delay(30000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Recording system monitoring cancelled");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during recording system monitoring: {Message}", ex.Message);
            }
        }
    }
}