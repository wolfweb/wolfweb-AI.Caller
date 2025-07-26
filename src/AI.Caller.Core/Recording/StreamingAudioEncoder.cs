using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 流式音频编码器实现，支持实时音频数据写入到文件
    /// </summary>
    public class StreamingAudioEncoder : IStreamingAudioEncoder
    {
        private readonly ILogger _logger;
        private readonly AudioEncodingOptions _options;
        private readonly object _lockObject = new object();
        
        private bool _isInitialized = false;
        private bool _disposed = false;
        private string? _outputFilePath;
        private AudioFormat? _inputFormat;
        private FileStream? _fileStream;
        private long _totalBytesWritten = 0;
        private long _dataChunkSizePosition = 0;
        private bool _headerWritten = false;
        
        public bool IsInitialized => _isInitialized;
        public string? OutputFilePath => _outputFilePath;
        public AudioEncodingOptions Options => _options;
        
        public event EventHandler<EncoderInitializedEventArgs>? EncoderInitialized;
        public event EventHandler<AudioWrittenEventArgs>? AudioWritten;
        public event EventHandler<EncodingProgressEventArgs>? EncodingProgress;
        public event EventHandler<EncodingErrorEventArgs>? EncodingError;
        
        public StreamingAudioEncoder(AudioEncodingOptions options, ILogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            ValidateOptions();
        }
        
        public async Task<bool> InitializeAsync(AudioFormat inputFormat, string outputPath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamingAudioEncoder));
                
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
                
                // 创建文件流
                _fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                
                // 写入WAV文件头（预留空间，稍后更新）
                await WriteWavHeaderAsync(0);
                _headerWritten = true;
                
                lock (_lockObject)
                {
                    _isInitialized = true;
                }
                
                _logger.LogInformation($"Streaming audio encoder initialized: {_options.Codec} -> {outputPath}");
                EncoderInitialized?.Invoke(this, new EncoderInitializedEventArgs(inputFormat, _options));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error initializing streaming audio encoder: {ex.Message}");
                EncodingError?.Invoke(this, new EncodingErrorEventArgs(RecordingErrorCode.InitializationFailed, ex.Message, ex));
                return false;
            }
        }
        
        public async Task<bool> WriteAudioFrameAsync(AudioFrame frame)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamingAudioEncoder));
                
            if (!_isInitialized || _fileStream == null)
            {
                _logger.LogWarning("Encoder not initialized, cannot write frame");
                return false;
            }
            
            if (frame?.Data == null || frame.Data.Length == 0)
            {
                _logger.LogWarning("Empty audio frame, skipping write");
                return true;
            }
            
            try
            {
                var writeTime = DateTime.UtcNow;
                
                // 转换音频数据格式（如果需要）
                var convertedData = ConvertAudioData(frame);
                
                // 实时写入音频数据
                await _fileStream.WriteAsync(convertedData, 0, convertedData.Length);
                
                lock (_lockObject)
                {
                    _totalBytesWritten += convertedData.Length;
                }
                
                // 触发事件
                AudioWritten?.Invoke(this, new AudioWrittenEventArgs(convertedData.Length, writeTime, true));
                EncodingProgress?.Invoke(this, new EncodingProgressEventArgs(convertedData.Length, writeTime));
                
                _logger.LogTrace($"Written audio frame: {convertedData.Length} bytes, total: {_totalBytesWritten}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error writing audio frame: {ex.Message}");
                AudioWritten?.Invoke(this, new AudioWrittenEventArgs(0, DateTime.UtcNow, false, ex.Message));
                EncodingError?.Invoke(this, new EncodingErrorEventArgs(RecordingErrorCode.EncodingFailed, ex.Message, ex));
                return false;
            }
        }
        
        public async Task<bool> FlushAsync()
        {
            if (_disposed || _fileStream == null)
                return false;
                
            try
            {
                await _fileStream.FlushAsync();
                _logger.LogTrace("Audio stream flushed");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error flushing audio stream: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> FinalizeAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamingAudioEncoder));
                
            if (!_isInitialized || _fileStream == null)
                return true;
                
            try
            {
                // 刷新所有数据
                await _fileStream.FlushAsync();
                
                // 更新WAV文件头中的数据大小
                if (_headerWritten && _dataChunkSizePosition > 0)
                {
                    await UpdateWavHeaderAsync();
                }
                
                // 关闭文件流
                _fileStream.Close();
                _fileStream.Dispose();
                _fileStream = null;
                
                lock (_lockObject)
                {
                    _isInitialized = false;
                }
                
                _logger.LogInformation($"Audio encoding finalized: {_outputFilePath}, {_totalBytesWritten} bytes written");
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
        
        private async Task WriteWavHeaderAsync(int dataSize)
        {
            if (_fileStream == null || _inputFormat == null)
                return;
                
            var header = CreateWavHeader(dataSize, _inputFormat.SampleRate, _inputFormat.Channels, _inputFormat.BitsPerSample);
            await _fileStream.WriteAsync(header, 0, header.Length);
            
            // 记录数据块大小的位置，用于后续更新
            _dataChunkSizePosition = 40; // WAV格式中数据块大小的位置
        }
        
        private async Task UpdateWavHeaderAsync()
        {
            if (_fileStream == null)
                return;
                
            try
            {
                // 保存当前位置
                var currentPosition = _fileStream.Position;
                
                // 更新文件总大小（位置4）
                _fileStream.Seek(4, SeekOrigin.Begin);
                var fileSizeBytes = BitConverter.GetBytes((int)(36 + _totalBytesWritten));
                await _fileStream.WriteAsync(fileSizeBytes, 0, 4);
                
                // 更新数据块大小（位置40）
                _fileStream.Seek(_dataChunkSizePosition, SeekOrigin.Begin);
                var dataSizeBytes = BitConverter.GetBytes((int)_totalBytesWritten);
                await _fileStream.WriteAsync(dataSizeBytes, 0, 4);
                
                // 恢复到原位置
                _fileStream.Seek(currentPosition, SeekOrigin.Begin);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update WAV header");
            }
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
            if (_options.SampleRate <= 0)
                throw new ArgumentException("Sample rate must be positive");
                
            if (_options.Channels <= 0)
                throw new ArgumentException("Channel count must be positive");
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
                    
                    _fileStream?.Dispose();
                    
                    _logger.LogInformation("StreamingAudioEncoder disposed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing StreamingAudioEncoder");
                }
            }
        }
    }
}