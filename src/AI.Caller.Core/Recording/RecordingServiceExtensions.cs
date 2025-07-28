using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 录音系统依赖注入扩展方法
    /// </summary>
    public static class RecordingServiceExtensions
    {
        /// <summary>
        /// 添加录音系统服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureOptions">录音选项配置</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddRecordingServices(
            this IServiceCollection services,
            Action<RecordingOptions>? configureOptions = null)
        {
            // 配置录音选项
            if (configureOptions != null)
            {
                services.Configure(configureOptions);
            }
            else
            {
                services.Configure<RecordingOptions>(options =>
                {
                    // 默认配置
                    options.Codec = AudioCodec.PCM_WAV;
                    options.SampleRate = 8000;
                    options.Channels = 1;
                    options.OutputDirectory = "./recordings";
                    options.FileNameTemplate = "{timestamp}_{caller}_{duration}";
                    options.MaxDuration = TimeSpan.FromHours(2);
                    options.MaxFileSize = 100 * 1024 * 1024; // 100MB
                });
            }

            // 注册核心录音服务
            services.AddSingleton<IAudioBridge, AudioBridge>();
            services.AddSingleton<IRecordingCore, RecordingCore>();
            services.AddSingleton<IAudioDataFlow, AudioDataFlow>();
            
            // 注册录音管理器的依赖
            services.AddSingleton<AudioRecorder>();
            services.AddSingleton<IAudioRecordingManager, AudioRecordingManager>();

            // 注册监控和状态服务
            services.AddSingleton<AudioDataFlowMonitor>();
            services.AddSingleton<IRecordingStatusService, RecordingStatusService>();

            // 注册文件管理和检测服务
            services.AddSingleton<RecordingFileManager>();
            services.AddSingleton<ZeroByteFileDetector>();

            // 注册音频处理服务
            services.AddSingleton<AudioMixer>();
            services.AddSingleton<AudioFormatConverter>();
            
            // 注册编码选项和流式编码器
            services.Configure<AudioEncodingOptions>(options =>
            {
                // 默认编码选项
                options.Quality = AudioQuality.Standard;
            });
            services.AddTransient<IStreamingAudioEncoder, StreamingAudioEncoder>();

            return services;
        }

        /// <summary>
        /// 添加录音系统服务到依赖注入容器（带详细配置）
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="recordingOptions">录音选项</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddRecordingServices(
            this IServiceCollection services,
            RecordingOptions recordingOptions)
        {
            return services.AddRecordingServices(options =>
            {
                options.Codec = recordingOptions.Codec;
                options.SampleRate = recordingOptions.SampleRate;
                options.Channels = recordingOptions.Channels;
                options.BitRate = recordingOptions.BitRate;
                options.OutputDirectory = recordingOptions.OutputDirectory;
                options.FileNameTemplate = recordingOptions.FileNameTemplate;
                options.AutoStart = recordingOptions.AutoStart;
                options.RecordBothParties = recordingOptions.RecordBothParties;
                options.MaxDuration = recordingOptions.MaxDuration;
                options.MaxFileSize = recordingOptions.MaxFileSize;
                options.Quality = recordingOptions.Quality;
                options.EnableNoiseReduction = recordingOptions.EnableNoiseReduction;
                options.EnableVolumeNormalization = recordingOptions.EnableVolumeNormalization;
            });
        }

        /// <summary>
        /// 添加录音系统的托管服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddRecordingHostedServices(this IServiceCollection services)
        {
            services.AddHostedService<RecordingBackgroundService>();
            return services;
        }

        /// <summary>
        /// 验证录音系统服务配置
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <returns>验证结果</returns>
        public static RecordingServiceValidationResult ValidateRecordingServices(this IServiceProvider serviceProvider)
        {
            var result = new RecordingServiceValidationResult();

            try
            {
                // 验证核心服务
                var audioBridge = serviceProvider.GetService<IAudioBridge>();
                if (audioBridge == null)
                {
                    result.Errors.Add("IAudioBridge service not registered");
                }

                var recordingCore = serviceProvider.GetService<IRecordingCore>();
                if (recordingCore == null)
                {
                    result.Errors.Add("IRecordingCore service not registered");
                }

                var audioDataFlow = serviceProvider.GetService<IAudioDataFlow>();
                if (audioDataFlow == null)
                {
                    result.Errors.Add("IAudioDataFlow service not registered");
                }

                var recordingManager = serviceProvider.GetService<IAudioRecordingManager>();
                if (recordingManager == null)
                {
                    result.Errors.Add("IAudioRecordingManager service not registered");
                }

                // 验证监控服务
                var dataFlowMonitor = serviceProvider.GetService<AudioDataFlowMonitor>();
                if (dataFlowMonitor == null)
                {
                    result.Errors.Add("AudioDataFlowMonitor service not registered");
                }

                var statusService = serviceProvider.GetService<IRecordingStatusService>();
                if (statusService == null)
                {
                    result.Errors.Add("IRecordingStatusService service not registered");
                }

                // 验证文件服务
                var fileManager = serviceProvider.GetService<RecordingFileManager>();
                if (fileManager == null)
                {
                    result.Errors.Add("RecordingFileManager service not registered");
                }

                var zeroByteDetector = serviceProvider.GetService<ZeroByteFileDetector>();
                if (zeroByteDetector == null)
                {
                    result.Errors.Add("ZeroByteFileDetector service not registered");
                }

                // 验证音频处理服务
                var audioMixer = serviceProvider.GetService<AudioMixer>();
                if (audioMixer == null)
                {
                    result.Errors.Add("AudioMixer service not registered");
                }

                var formatConverter = serviceProvider.GetService<AudioFormatConverter>();
                if (formatConverter == null)
                {
                    result.Errors.Add("AudioFormatConverter service not registered");
                }

                // 验证配置
                var recordingOptions = serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<RecordingOptions>>();
                if (recordingOptions?.Value != null)
                {
                    var validation = recordingOptions.Value.Validate();
                    if (!validation.IsValid)
                    {
                        result.Errors.AddRange(validation.Errors.Select(e => $"RecordingOptions validation: {e}"));
                    }
                }
                else
                {
                    result.Errors.Add("RecordingOptions not configured");
                }

                result.IsValid = result.Errors.Count == 0;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Validation exception: {ex.Message}");
                result.IsValid = false;
            }

            return result;
        }
    }

    /// <summary>
    /// 录音服务验证结果
    /// </summary>
    public class RecordingServiceValidationResult
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
        /// 验证时间
        /// </summary>
        public DateTime ValidationTime { get; set; } = DateTime.UtcNow;

        public override string ToString()
        {
            return $"Valid: {IsValid}, Errors: {Errors.Count}";
        }
    }
}