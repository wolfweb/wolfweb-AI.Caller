using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 音频编码器工厂
    /// </summary>
    public interface IAudioEncoderFactory
    {
        /// <summary>
        /// 创建流式音频编码器
        /// </summary>
        /// <param name="options">编码选项</param>
        /// <returns>流式音频编码器实例</returns>
        IStreamingAudioEncoder CreateStreamingEncoder(AudioEncodingOptions options);
    }
    
    /// <summary>
    /// 音频编码器工厂实现
    /// </summary>
    public class AudioEncoderFactory : IAudioEncoderFactory
    {
        private readonly ILogger<StreamingAudioEncoder> _logger;
        
        public AudioEncoderFactory(ILogger<StreamingAudioEncoder> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public IStreamingAudioEncoder CreateStreamingEncoder(AudioEncodingOptions options)
        {
            return new StreamingAudioEncoder(options, _logger);
        }
    }
}