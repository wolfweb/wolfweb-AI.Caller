using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Collections.Concurrent;
using System.Net;

namespace AI.Caller.Core.Recording
{    
    public class AudioRecorder : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<AudioFrame> _audioBuffer;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly object _lockObject = new object();
        
        private bool _isCapturing = false;
        private bool _disposed = false;
        private uint _sequenceNumber = 0;
             
        public event EventHandler<AudioDataEventArgs>? AudioDataReceived;
        
        public event EventHandler<BufferOverflowEventArgs>? BufferOverflow;
        
        public bool IsCapturing => _isCapturing;
        
        public int BufferedFrameCount => _audioBuffer.Count;
        
        public int MaxBufferSize { get; set; } = 1000;
        
        public AudioRecorder(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _audioBuffer = new ConcurrentQueue<AudioFrame>();
            _cancellationTokenSource = new CancellationTokenSource();
        }
        
        public Task StartCaptureAsync(params AudioSource[] sources)
        {
            lock (_lockObject)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(AudioRecorder));
                    
                if (_isCapturing)
                    return Task.CompletedTask;
                
                _isCapturing = true;
                _logger.LogInformation($"Started audio capture for sources: {string.Join(", ", sources)}");
            }
            
            return Task.CompletedTask;
        }
        
        public Task StopCaptureAsync()
        {
            lock (_lockObject)
            {
                if (!_isCapturing)
                    return Task.CompletedTask;
                
                _isCapturing = false;
                _logger.LogInformation("Stopped audio capture");
            }
            
            return Task.CompletedTask;
        }
        
        public void OnRtpAudioReceived(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket, AudioSource source)
        {
            if (!_isCapturing || mediaType != SDPMediaTypesEnum.audio)
                return;
                
            // 验证RTP包
            if (rtpPacket?.Payload == null || rtpPacket.Payload.Length == 0)
            {
                _logger.LogTrace($"Received empty RTP packet from {source}");
                return;
            }
            
            // 检查包大小是否合理（避免异常大的包）
            if (rtpPacket.Payload.Length > 8192) // 8KB限制
            {
                _logger.LogWarning($"Received oversized RTP packet ({rtpPacket.Payload.Length} bytes) from {source}, skipping");
                return;
            }
                
            try
            {
                // 创建音频格式（基于RTP包的信息）
                var audioFormat = CreateAudioFormatFromRtp(rtpPacket);
                
                // 验证音频格式
                if (!IsValidAudioFormat(audioFormat))
                {
                    _logger.LogWarning($"Invalid audio format from {source}: {audioFormat}");
                    return;
                }
                
                // 创建音频帧
                var audioFrame = new AudioFrame(rtpPacket.Payload, audioFormat, source)
                {
                    SequenceNumber = ++_sequenceNumber,
                    Timestamp = DateTime.UtcNow
                };
                
                // 添加到缓冲区
                AddToBuffer(audioFrame);
                
                // 触发事件
                AudioDataReceived?.Invoke(this, new AudioDataEventArgs(audioFrame, remote));
                
                _logger.LogTrace($"Captured RTP audio frame: {audioFrame.Data.Length} bytes from {source}, seq: {audioFrame.SequenceNumber}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing RTP audio from {source}: {ex.Message}");
            }
        }
        
        public void OnWebRtcAudioReceived(byte[] audioData, AudioFormat format, AudioSource source)
        {
            if (!_isCapturing)
                return;
                
            // 验证WebRTC音频数据
            if (audioData == null || audioData.Length == 0)
            {
                _logger.LogTrace($"Received empty WebRTC audio data from {source}");
                return;
            }
            
            if (audioData.Length > 8192) // 8KB限制
            {
                _logger.LogWarning($"Received oversized WebRTC audio data ({audioData.Length} bytes) from {source}, skipping");
                return;
            }
                
            try
            {
                // 验证音频格式
                if (!IsValidAudioFormat(format))
                {
                    _logger.LogWarning($"Invalid WebRTC audio format from {source}: {format}");
                    return;
                }
                
                // 创建音频帧
                var audioFrame = new AudioFrame(audioData, format, source)
                {
                    SequenceNumber = ++_sequenceNumber,
                    Timestamp = DateTime.UtcNow
                };
                
                // 添加到缓冲区
                AddToBuffer(audioFrame);
                
                // 触发事件
                AudioDataReceived?.Invoke(this, new AudioDataEventArgs(audioFrame, null));
                
                _logger.LogTrace($"Captured WebRTC audio frame: {audioFrame.Data.Length} bytes from {source}, seq: {audioFrame.SequenceNumber}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing WebRTC audio from {source}: {ex.Message}");
            }
        }
        
        public List<AudioFrame> GetBufferedFrames(int maxFrames = int.MaxValue)
        {
            var frames = new List<AudioFrame>();
            int count = 0;
            
            while (count < maxFrames && _audioBuffer.TryDequeue(out var frame))
            {
                frames.Add(frame);
                count++;
            }
            
            return frames;
        }
        
        public void ClearBuffer()
        {
            while (_audioBuffer.TryDequeue(out _))
            {
                // 清空队列
            }
            
            _logger.LogDebug("Audio buffer cleared");
        }
        
        public AudioSourceStats GetAudioSourceStats()
        {
            var frames = _audioBuffer.ToArray();
            var stats = new AudioSourceStats();
            
            foreach (var frame in frames)
            {
                switch (frame.Source)
                {
                    case AudioSource.RTP_Incoming:
                        stats.RtpIncomingFrames++;
                        stats.RtpIncomingBytes += frame.Data.Length;
                        break;
                    case AudioSource.RTP_Outgoing:
                        stats.RtpOutgoingFrames++;
                        stats.RtpOutgoingBytes += frame.Data.Length;
                        break;
                    case AudioSource.WebRTC_Incoming:
                        stats.WebRtcIncomingFrames++;
                        stats.WebRtcIncomingBytes += frame.Data.Length;
                        break;
                    case AudioSource.WebRTC_Outgoing:
                        stats.WebRtcOutgoingFrames++;
                        stats.WebRtcOutgoingBytes += frame.Data.Length;
                        break;
                }
            }
            
            stats.TotalFrames = frames.Length;
            stats.BufferSize = _audioBuffer.Count;
            stats.MaxBufferSize = MaxBufferSize;
            
            return stats;
        }
        
        private void AddToBuffer(AudioFrame frame)
        {
            _audioBuffer.Enqueue(frame);
            
            // 检查缓冲区溢出
            if (_audioBuffer.Count > MaxBufferSize)
            {
                // 移除最旧的帧，保持缓冲区在80%容量
                var targetSize = (int)(MaxBufferSize * 0.8);
                var removedCount = 0;
                
                while (_audioBuffer.Count > targetSize && _audioBuffer.TryDequeue(out _))
                {
                    removedCount++;
                }
                
                _logger.LogWarning($"Audio buffer overflow, removed {removedCount} old frames, current size: {_audioBuffer.Count}");
                BufferOverflow?.Invoke(this, new BufferOverflowEventArgs(removedCount, _audioBuffer.Count));
                
                // 如果缓冲区持续溢出，考虑动态调整大小
                if (removedCount > MaxBufferSize * 0.5)
                {
                    var newMaxSize = Math.Min(MaxBufferSize * 2, 2000); // 最大不超过2000
                    if (newMaxSize > MaxBufferSize)
                    {
                        _logger.LogInformation($"Dynamically increasing buffer size from {MaxBufferSize} to {newMaxSize}");
                        MaxBufferSize = newMaxSize;
                    }
                }
            }
        }
        
        private AudioFormat CreateAudioFormatFromRtp(RTPPacket rtpPacket)
        {
            // 根据RTP包的payload type推断音频格式
            // 这里使用常见的音频格式作为默认值
            var sampleRate = rtpPacket.Header.PayloadType switch
            {
                0 => 8000,   // PCMU
                8 => 8000,   // PCMA
                9 => 8000,   // G722
                _ => 8000    // 默认
            };
            
            var sampleFormat = rtpPacket.Header.PayloadType switch
            {
                0 => AudioSampleFormat.ULAW,  // PCMU
                8 => AudioSampleFormat.ALAW,  // PCMA
                _ => AudioSampleFormat.PCM    // 默认
            };
            
            return new AudioFormat(sampleRate, 1, 16, sampleFormat);
        }
        
        private bool IsValidAudioFormat(AudioFormat format)
        {
            // 验证音频格式的合理性
            if (format == null)
                return false;
                
            // 检查采样率范围
            if (format.SampleRate < 8000 || format.SampleRate > 48000)
                return false;
                
            // 检查声道数
            if (format.Channels < 1 || format.Channels > 2)
                return false;
                
            // 检查位深度
            if (format.BitsPerSample != 8 && format.BitsPerSample != 16 && format.BitsPerSample != 24 && format.BitsPerSample != 32)
                return false;
                
            return true;
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
                _isCapturing = false;
                
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                
                ClearBuffer();
                
                _logger.LogInformation("AudioRecorder disposed");
            }
        }
    }
}