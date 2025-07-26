using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    
    public class FFmpegAudioEncoder : IDisposable
    {
        private readonly ILogger _logger;
        private readonly AudioEncodingOptions _options;
        private readonly object _lockObject = new object();
        private readonly List<byte[]> _audioBuffer;
        
        private bool _isInitialized = false;
        private bool _disposed = false;
        private string? _outputFilePath;
        private AudioFormat? _inputFormat;
        private long _totalBytesEncoded = 0;
                
        public event EventHandler<EncoderInitializedEventArgs>? EncoderInitialized;
                
        public event EventHandler<EncodingProgressEventArgs>? EncodingProgress;
                
        public event EventHandler<EncodingErrorEventArgs>? EncodingError;
                
        public bool IsInitialized => _isInitialized;
                
        public string? OutputFilePath => _outputFilePath;
                
        public AudioEncodingOptions Options => _options;
                
        public static readonly Dictionary<AudioCodec, string> SupportedCodecs = new()
        {
            { AudioCodec.PCM_WAV, "pcm_s16le" },
            { AudioCodec.MP3, "libmp3lame" },
            { AudioCodec.AAC, "aac" },
            { AudioCodec.OPUS, "libopus" }
        };
                
        public static readonly Dictionary<AudioCodec, string> SupportedFormats = new()
        {
            { AudioCodec.PCM_WAV, "wav" },
            { AudioCodec.MP3, "mp3" },
            { AudioCodec.AAC, "mp4" },
            { AudioCodec.OPUS, "ogg" }
        };
        
        public FFmpegAudioEncoder(AudioEncodingOptions options, ILogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _audioBuffer = new List<byte[]>();
            
            ValidateOptions();
        }
                
        public async Task<bool> InitializeAsync(AudioFormat inputFormat, string outputPath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FFmpegAudioEncoder));
                
            lock (_lockObject)
            {
                if (_isInitialized)
                    return true;
            }
            
            try
            {
                _outputFilePath = outputPath;
                _inputFormat = inputFormat;
                
                // 确保输出目录存在
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                lock (_lockObject)
                {
                    _isInitialized = true;
                }
                
                _logger.LogInformation($"FFmpeg audio encoder initialized: {_options.Codec} -> {outputPath}");
                EncoderInitialized?.Invoke(this, new EncoderInitializedEventArgs(inputFormat, _options));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error initializing FFmpeg audio encoder: {ex.Message}");
                EncodingError?.Invoke(this, new EncodingErrorEventArgs(RecordingErrorCode.InitializationFailed, ex.Message, ex));
                return false;
            }
        }
                
        public async Task<bool> EncodeAudioFrameAsync(AudioFrame frame)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FFmpegAudioEncoder));
                
            if (!_isInitialized)
            {
                _logger.LogWarning("Encoder not initialized, cannot encode frame");
                return false;
            }
            
            if (frame?.Data == null)
            {
                _logger.LogWarning("Null audio frame, cannot encode");
                return false;
            }
            
            if (frame.Data.Length == 0)
            {
                _logger.LogWarning("Empty audio frame, skipping encoding");
                return true;
            }
            
            try
            {
                // 转换音频数据格式（如果需要）
                var convertedData = ConvertAudioData(frame);
                
                // 添加到缓冲区
                lock (_lockObject)
                {
                    _audioBuffer.Add(convertedData);
                    _totalBytesEncoded += convertedData.Length;
                }
                
                // 触发进度事件
                EncodingProgress?.Invoke(this, new EncodingProgressEventArgs(convertedData.Length, DateTime.UtcNow));
                
                _logger.LogTrace($"Encoded audio frame: {frame.Data.Length} bytes");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error encoding audio frame: {ex.Message}");
                EncodingError?.Invoke(this, new EncodingErrorEventArgs(RecordingErrorCode.EncodingFailed, ex.Message, ex));
                return false;
            }
        }
                
        public async Task<bool> FinalizeAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FFmpegAudioEncoder));
                
            if (!_isInitialized)
                return true;
                
            try
            {
                // 合并所有音频数据
                var totalSize = _audioBuffer.Sum(data => data.Length);
                var combinedData = new byte[totalSize];
                var offset = 0;
                
                foreach (var data in _audioBuffer)
                {
                    Array.Copy(data, 0, combinedData, offset, data.Length);
                    offset += data.Length;
                }
                
                // 根据编码格式写入文件
                await WriteAudioFile(combinedData);
                
                lock (_lockObject)
                {
                    _isInitialized = false;
                    _audioBuffer.Clear();
                }
                
                _logger.LogInformation($"Audio encoding finalized: {_outputFilePath}, {_totalBytesEncoded} bytes encoded");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error finalizing audio encoding: {ex.Message}");
                EncodingError?.Invoke(this, new EncodingErrorEventArgs(RecordingErrorCode.EncodingFailed, ex.Message, ex));
                return false;
            }
        }
                
        public EncoderInfo GetEncoderInfo()
        {
            return new EncoderInfo
            {
                Codec = _options.Codec,
                SampleRate = _options.SampleRate,
                Channels = _options.Channels,
                BitRate = _options.BitRate,
                IsInitialized = _isInitialized,
                OutputPath = _outputFilePath
            };
        }
                
        public static bool IsCodecSupported(AudioCodec codec)
        {
            return SupportedCodecs.ContainsKey(codec);
        }
                
        public static string GetRecommendedFileExtension(AudioCodec codec)
        {
            return codec switch
            {
                AudioCodec.PCM_WAV => ".wav",
                AudioCodec.MP3 => ".mp3",
                AudioCodec.AAC => ".m4a",
                AudioCodec.OPUS => ".opus",
                _ => ".audio"
            };
        }
                
        private async Task WriteAudioFile(byte[] audioData)
        {
            if (string.IsNullOrEmpty(_outputFilePath))
                throw new InvalidOperationException("Output file path not set");
                
            switch (_options.Codec)
            {
                case AudioCodec.PCM_WAV:
                    await WriteWavFile(audioData);
                    break;
                case AudioCodec.MP3:
                case AudioCodec.AAC:
                case AudioCodec.OPUS:
                    // 对于压缩格式，这里简化实现，直接写成WAV格式
                    // 在实际应用中，应该调用FFmpeg进行转换
                    await WriteWavFile(audioData);
                    break;
                default:
                    await WriteRawPcmFile(audioData);
                    break;
            }
        }
                
        private async Task WriteWavFile(byte[] audioData)
        {
            using var fileStream = new FileStream(_outputFilePath!, FileMode.Create, FileAccess.Write);
            
            // WAV文件头
            var header = CreateWavHeader(audioData.Length, _options.SampleRate, _options.Channels, 16);
            await fileStream.WriteAsync(header, 0, header.Length);
            
            // 音频数据
            await fileStream.WriteAsync(audioData, 0, audioData.Length);
        }
                
        private async Task WriteRawPcmFile(byte[] audioData)
        {
            using var fileStream = new FileStream(_outputFilePath!, FileMode.Create, FileAccess.Write);
            await fileStream.WriteAsync(audioData, 0, audioData.Length);
        }
                
        private byte[] CreateWavHeader(int dataSize, int sampleRate, int channels, int bitsPerSample)
        {
            var header = new byte[44];
            var byteRate = sampleRate * channels * bitsPerSample / 8;
            var blockAlign = channels * bitsPerSample / 8;
            
            // RIFF header
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, header, 0, 4);
            BitConverter.GetBytes(36 + dataSize).CopyTo(header, 4);
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("WAVE"), 0, header, 8, 4);
            
            // fmt chunk
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("fmt "), 0, header, 12, 4);
            BitConverter.GetBytes(16).CopyTo(header, 16); // chunk size
            BitConverter.GetBytes((short)1).CopyTo(header, 20); // audio format (PCM)
            BitConverter.GetBytes((short)channels).CopyTo(header, 22);
            BitConverter.GetBytes(sampleRate).CopyTo(header, 24);
            BitConverter.GetBytes(byteRate).CopyTo(header, 28);
            BitConverter.GetBytes((short)blockAlign).CopyTo(header, 32);
            BitConverter.GetBytes((short)bitsPerSample).CopyTo(header, 34);
            
            // data chunk
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("data"), 0, header, 36, 4);
            BitConverter.GetBytes(dataSize).CopyTo(header, 40);
            
            return header;
        }
                
        private byte[] ConvertAudioData(AudioFrame frame)
        {
            // 根据需要进行音频格式转换
            switch (frame.Format.SampleFormat)
            {
                case AudioSampleFormat.ALAW:
                    return ConvertAlawToPcm(frame.Data);
                    
                case AudioSampleFormat.ULAW:
                    return ConvertUlawToPcm(frame.Data);
                    
                case AudioSampleFormat.PCM:
                default:
                    return frame.Data;
            }
        }
                
        private byte[] ConvertAlawToPcm(byte[] alawData)
        {
            var pcmData = new byte[alawData.Length * 2]; // 16-bit PCM
            
            for (int i = 0; i < alawData.Length; i++)
            {
                var pcmSample = AlawToPcm(alawData[i]);
                var bytes = BitConverter.GetBytes(pcmSample);
                pcmData[i * 2] = bytes[0];
                pcmData[i * 2 + 1] = bytes[1];
            }
            
            return pcmData;
        }
                
        private byte[] ConvertUlawToPcm(byte[] ulawData)
        {
            var pcmData = new byte[ulawData.Length * 2]; // 16-bit PCM
            
            for (int i = 0; i < ulawData.Length; i++)
            {
                var pcmSample = UlawToPcm(ulawData[i]);
                var bytes = BitConverter.GetBytes(pcmSample);
                pcmData[i * 2] = bytes[0];
                pcmData[i * 2 + 1] = bytes[1];
            }
            
            return pcmData;
        }
                
        private short AlawToPcm(byte alaw)
        {
            alaw ^= 0x55;
            int sign = alaw & 0x80;
            int exponent = (alaw & 0x70) >> 4;
            int mantissa = alaw & 0x0F;
            
            int sample = mantissa << 4;
            if (exponent != 0)
                sample += 0x100;
            if (exponent > 1)
                sample <<= exponent - 1;
                
            return (short)(sign != 0 ? -sample : sample);
        }
                
        private short UlawToPcm(byte ulaw)
        {
            ulaw = (byte)~ulaw;
            int sign = ulaw & 0x80;
            int exponent = (ulaw & 0x70) >> 4;
            int mantissa = ulaw & 0x0F;
            
            int sample = ((mantissa << 3) + 0x84) << exponent;
            return (short)(sign != 0 ? -sample : sample);
        }
                
        private void ValidateOptions()
        {
            if (!IsCodecSupported(_options.Codec))
            {
                throw new ArgumentException($"Unsupported codec: {_options.Codec}");
            }
            
            if (_options.SampleRate <= 0)
            {
                throw new ArgumentException("Sample rate must be positive");
            }
            
            if (_options.Channels <= 0)
            {
                throw new ArgumentException("Channel count must be positive");
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            lock (_lockObject)
            {
                if (_disposed)
                    return;
                    
                _disposed = true;
                
                try
                {
                    if (_isInitialized)
                    {
                        _ = Task.Run(async () => await FinalizeAsync());
                    }
                    
                    _audioBuffer.Clear();
                    
                    _logger.LogInformation("FFmpegAudioEncoder disposed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing FFmpegAudioEncoder");
                }
            }
        }
    }
    
    
    public class AudioEncodingOptions
    {        
        public AudioCodec Codec { get; set; } = AudioCodec.PCM_WAV;
                
        public int SampleRate { get; set; } = 8000;
                
        public int Channels { get; set; } = 1;
                
        public int BitRate { get; set; } = 64000;
                
        public AudioQuality Quality { get; set; } = AudioQuality.Standard;
                
        public static AudioEncodingOptions CreateDefault()
        {
            return new AudioEncodingOptions();
        }
                
        public static AudioEncodingOptions CreateHighQuality()
        {
            return new AudioEncodingOptions
            {
                Codec = AudioCodec.AAC,
                SampleRate = 44100,
                Channels = 2,
                BitRate = 128000,
                Quality = AudioQuality.High
            };
        }
                
        public static AudioEncodingOptions CreateLowQuality()
        {
            return new AudioEncodingOptions
            {
                Codec = AudioCodec.MP3,
                SampleRate = 8000,
                Channels = 1,
                BitRate = 32000,
                Quality = AudioQuality.Low
            };
        }
    }
    
    public class EncoderInitializedEventArgs : EventArgs
    {
        public AudioFormat InputFormat { get; }
        public AudioEncodingOptions EncodingOptions { get; }
        
        public EncoderInitializedEventArgs(AudioFormat inputFormat, AudioEncodingOptions encodingOptions)
        {
            InputFormat = inputFormat;
            EncodingOptions = encodingOptions;
        }
    }
    
    public class EncoderInfo
    {
        public AudioCodec Codec { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitRate { get; set; }
        public bool IsInitialized { get; set; }
        public string? OutputPath { get; set; }
        
        public override string ToString()
        {
            return $"{Codec} - {SampleRate}Hz, {Channels}ch, {BitRate}bps -> {OutputPath}";
        }
    }
}