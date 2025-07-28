using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Recording;

namespace AI.Caller.Core.Tests.Recording
{
    /// <summary>
    /// 录音服务扩展方法测试
    /// </summary>
    public class RecordingServiceExtensionsTests : IDisposable
    {
        private readonly ServiceCollection _services;
        private ServiceProvider? _serviceProvider;

        public RecordingServiceExtensionsTests()
        {
            _services = new ServiceCollection();
            _services.AddLogging();
        }

        public void Dispose()
        {
            _serviceProvider?.Dispose();
        }

        [Fact]
        public void AddRecordingServices_ShouldRegisterAllRequiredServices()
        {
            // Arrange & Act
            _services.AddRecordingServices(options =>
            {
                options.Codec = AudioCodec.PCM_WAV;
                options.SampleRate = 8000;
                options.Channels = 1;
                options.OutputDirectory = "./test-recordings";
            });

            _serviceProvider = _services.BuildServiceProvider();

            // Assert
            Assert.NotNull(_serviceProvider.GetService<IAudioBridge>());
            Assert.NotNull(_serviceProvider.GetService<IRecordingCore>());
            Assert.NotNull(_serviceProvider.GetService<IAudioDataFlow>());
            Assert.NotNull(_serviceProvider.GetService<IAudioRecordingManager>());
            Assert.NotNull(_serviceProvider.GetService<AudioDataFlowMonitor>());
            Assert.NotNull(_serviceProvider.GetService<IRecordingStatusService>());
            Assert.NotNull(_serviceProvider.GetService<RecordingFileManager>());
            Assert.NotNull(_serviceProvider.GetService<ZeroByteFileDetector>());
            Assert.NotNull(_serviceProvider.GetService<AudioMixer>());
            Assert.NotNull(_serviceProvider.GetService<AudioFormatConverter>());
            Assert.NotNull(_serviceProvider.GetService<IStreamingAudioEncoder>());
        }

        [Fact]
        public void AddRecordingServices_WithRecordingOptions_ShouldConfigureCorrectly()
        {
            // Arrange
            var recordingOptions = new RecordingOptions
            {
                Codec = AudioCodec.PCM_WAV,
                SampleRate = 16000,
                Channels = 2,
                OutputDirectory = "./high-quality-recordings",
                MaxDuration = TimeSpan.FromHours(1)
            };

            // Act
            _services.AddRecordingServices(recordingOptions);
            _serviceProvider = _services.BuildServiceProvider();

            // Assert
            var configuredOptions = _serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<RecordingOptions>>();
            Assert.NotNull(configuredOptions);
            Assert.Equal(AudioCodec.PCM_WAV, configuredOptions.Value.Codec);
            Assert.Equal(16000, configuredOptions.Value.SampleRate);
            Assert.Equal(2, configuredOptions.Value.Channels);
            Assert.Equal("./high-quality-recordings", configuredOptions.Value.OutputDirectory);
            Assert.Equal(TimeSpan.FromHours(1), configuredOptions.Value.MaxDuration);
        }

        [Fact]
        public void AddRecordingHostedServices_ShouldRegisterBackgroundService()
        {
            // Arrange & Act
            _services.AddRecordingServices();
            _services.AddRecordingHostedServices();
            _serviceProvider = _services.BuildServiceProvider();

            // Assert
            var hostedServices = _serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
            Assert.Contains(hostedServices, service => service is RecordingBackgroundService);
        }

        [Fact]
        public void ValidateRecordingServices_WithValidConfiguration_ShouldReturnValid()
        {
            // Arrange
            _services.AddRecordingServices(options =>
            {
                options.Codec = AudioCodec.PCM_WAV;
                options.SampleRate = 8000;
                options.Channels = 1;
                options.OutputDirectory = "./test-recordings";
                options.FileNameTemplate = "test_{timestamp}.wav";
            });
            _serviceProvider = _services.BuildServiceProvider();

            // Act
            var result = _serviceProvider.ValidateRecordingServices();

            // Assert
            Assert.True(result.IsValid, $"Validation failed with errors: {string.Join(", ", result.Errors)}");
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateRecordingServices_WithInvalidConfiguration_ShouldReturnInvalid()
        {
            // Arrange
            _services.AddRecordingServices(options =>
            {
                options.SampleRate = -1; // 无效值
                options.Channels = 0; // 无效值
                options.OutputDirectory = ""; // 无效值
                options.FileNameTemplate = ""; // 无效值
            });
            _serviceProvider = _services.BuildServiceProvider();

            // Act
            var result = _serviceProvider.ValidateRecordingServices();

            // Assert
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void LoadFromConfiguration_WithValidConfig_ShouldReturnCorrectOptions()
        {
            // Arrange
            var configData = new Dictionary<string, string?>
            {
                ["Recording:Codec"] = "PCM_WAV",
                ["Recording:SampleRate"] = "8000",
                ["Recording:Channels"] = "1",
                ["Recording:OutputDirectory"] = "./config-recordings",
                ["Recording:FileNameTemplate"] = "config_{timestamp}.wav",
                ["Recording:MaxDuration"] = "01:30:00"
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            // Act
            var options = RecordingServiceConfiguration.LoadFromConfiguration(configuration);

            // Assert
            Assert.Equal(AudioCodec.PCM_WAV, options.Codec);
            Assert.Equal(8000, options.SampleRate);
            Assert.Equal(1, options.Channels);
            Assert.Equal("./config-recordings", options.OutputDirectory);
            Assert.Equal("config_{timestamp}.wav", options.FileNameTemplate);
            Assert.Equal(TimeSpan.FromMinutes(90), options.MaxDuration);
        }

        [Fact]
        public void LoadFromConfiguration_WithInvalidConfig_ShouldThrowException()
        {
            // Arrange
            var configData = new Dictionary<string, string?>
            {
                ["Recording:SampleRate"] = "-1", // 无效值
                ["Recording:Channels"] = "0", // 无效值
                ["Recording:OutputDirectory"] = "", // 无效值
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                RecordingServiceConfiguration.LoadFromConfiguration(configuration));
        }

        [Fact]
        public void AddCompleteRecordingConfiguration_ShouldConfigureAllServices()
        {
            // Arrange
            var configData = new Dictionary<string, string?>
            {
                ["Recording:Codec"] = "PCM_WAV",
                ["Recording:SampleRate"] = "8000",
                ["Recording:Channels"] = "1",
                ["Recording:OutputDirectory"] = "./complete-recordings",
                ["Recording:FileNameTemplate"] = "complete_{timestamp}.wav"
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            // Act
            _services.AddCompleteRecordingConfiguration(configuration, options =>
            {
                options.AutoStart = true; // 覆盖配置
            });
            _serviceProvider = _services.BuildServiceProvider();

            // Assert
            var configuredOptions = _serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<RecordingOptions>>();
            Assert.NotNull(configuredOptions);
            Assert.Equal("./complete-recordings", configuredOptions.Value.OutputDirectory);
            Assert.True(configuredOptions.Value.AutoStart); // 应该被覆盖

            // 验证所有服务都已注册
            Assert.NotNull(_serviceProvider.GetService<IAudioBridge>());
            Assert.NotNull(_serviceProvider.GetService<IRecordingCore>());
            Assert.NotNull(_serviceProvider.GetService<AudioDataFlowMonitor>());

            // 验证托管服务已注册
            var hostedServices = _serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
            Assert.Contains(hostedServices, service => service is RecordingBackgroundService);
        }

        [Fact]
        public async Task ValidateRuntimeConfigurationAsync_WithValidSetup_ShouldReturnValid()
        {
            // Arrange
            _services.AddRecordingServices(options =>
            {
                options.Codec = AudioCodec.PCM_WAV;
                options.SampleRate = 8000;
                options.Channels = 1;
                options.OutputDirectory = Path.GetTempPath(); // 使用临时目录确保有写权限
                options.FileNameTemplate = "runtime_test_{timestamp}.wav";
            });
            _serviceProvider = _services.BuildServiceProvider();

            // Act
            var result = await RecordingServiceConfiguration.ValidateRuntimeConfigurationAsync(_serviceProvider);

            // Assert
            Assert.True(result.IsValid, $"Runtime validation failed with errors: {string.Join(", ", result.Errors)}");
        }

        [Fact]
        public async Task ValidateRuntimeConfigurationAsync_WithInvalidDirectory_ShouldReturnInvalid()
        {
            // Arrange
            _services.AddRecordingServices(options =>
            {
                options.Codec = AudioCodec.PCM_WAV;
                options.SampleRate = 8000;
                options.Channels = 1;
                options.OutputDirectory = "Z:\\NonExistentDrive\\InvalidPath"; // 无效路径
                options.FileNameTemplate = "runtime_test_{timestamp}.wav";
            });
            _serviceProvider = _services.BuildServiceProvider();

            // Act
            var result = await RecordingServiceConfiguration.ValidateRuntimeConfigurationAsync(_serviceProvider);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("output directory"));
        }

        [Fact]
        public void RecordingServiceValidationResult_ToString_ShouldFormatCorrectly()
        {
            // Arrange
            var result = new RecordingServiceValidationResult
            {
                IsValid = false,
                Errors = new List<string> { "Error 1", "Error 2" }
            };

            // Act
            var formatted = result.ToString();

            // Assert
            Assert.Contains("Valid: False", formatted);
            Assert.Contains("Errors: 2", formatted);
        }

        [Fact]
        public void RecordingConfigurationValidationResult_ToString_ShouldFormatCorrectly()
        {
            // Arrange
            var result = new RecordingConfigurationValidationResult
            {
                IsValid = true,
                Errors = new List<string>(),
                Warnings = new List<string> { "Warning 1" }
            };

            // Act
            var formatted = result.ToString();

            // Assert
            Assert.Contains("Valid: True", formatted);
            Assert.Contains("Errors: 0", formatted);
            Assert.Contains("Warnings: 1", formatted);
        }
    }
}