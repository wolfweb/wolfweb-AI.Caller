using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{    
    public class RecordingExample
    {
        private readonly ILogger _logger;
        private AudioRecordingManager? _recordingManager;
        
        public RecordingExample(ILogger logger)
        {
            _logger = logger;
        }
              
        public async Task<string?> BasicRecordingExampleAsync()
        {
            // 1. 创建录音组件
            var audioRecorder = new AudioRecorder(_logger);
            var audioMixer = new AudioMixer(_logger);
            var audioEncoder = new FFmpegAudioEncoder(AudioEncodingOptions.CreateDefault(), _logger);
            var fileManager = new RecordingFileManager(new RecordingStorageOptions(), _logger);
            var formatConverter = new AudioFormatConverter(_logger);
            
            // 2. 创建录音管理器
            _recordingManager = new AudioRecordingManager(
                audioRecorder, audioMixer, audioEncoder, fileManager, formatConverter, _logger);
            
            // 3. 配置录音选项
            var options = new RecordingOptions
            {
                Codec = AudioCodec.PCM_WAV,
                SampleRate = 8000,
                Channels = 1,
                OutputDirectory = "./recordings",
                FileNameTemplate = "call_{timestamp}",
                MaxDuration = TimeSpan.FromMinutes(30)
            };
            
            // 4. 开始录音
            var started = await _recordingManager.StartRecordingAsync(options);
            if (!started)
            {
                _logger.LogError("Failed to start recording");
                return null;
            }
            
            _logger.LogInformation("Recording started successfully");
            
            // 5. 模拟录音过程（实际应用中会有真实的音频数据）
            await Task.Delay(5000); // 录音5秒
            
            // 6. 停止录音
            var filePath = await _recordingManager.StopRecordingAsync();
            if (filePath != null)
            {
                _logger.LogInformation($"Recording saved to: {filePath}");
            }
            
            return filePath;
        }
         
        public async Task<string?> HighQualityRecordingExampleAsync()
        {
            var audioRecorder = new AudioRecorder(_logger);
            var audioMixer = new AudioMixer(_logger);
            var audioEncoder = new FFmpegAudioEncoder(AudioEncodingOptions.CreateHighQuality(), _logger);
            var fileManager = new RecordingFileManager(new RecordingStorageOptions(), _logger);
            var formatConverter = new AudioFormatConverter(_logger);
            
            _recordingManager = new AudioRecordingManager(
                audioRecorder, audioMixer, audioEncoder, fileManager, formatConverter, _logger);
            
            var options = new RecordingOptions
            {
                Codec = AudioCodec.AAC,
                SampleRate = 44100,
                Channels = 2,
                Quality = AudioQuality.High,
                OutputDirectory = "./high_quality_recordings"
            };
            
            var started = await _recordingManager.StartRecordingAsync(options);
            if (started)
            {
                await Task.Delay(10000); // 录音10秒
                return await _recordingManager.StopRecordingAsync();
            }
            
            return null;
        }
        
        public void Dispose()
        {
            _recordingManager?.Dispose();
        }
    }
}