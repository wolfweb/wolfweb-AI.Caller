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
                
            try
            {
                // 创建音频格式（基于RTP包的信息）
                var audioFormat = CreateAudioFormatFromRtp(rtpPacket);
                
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
                
                _logger.LogTrace($"Captured RTP audio frame: {audioFrame.Data.Length} bytes from {source}");
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
                
            try
            {
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
                
                _logger.LogTrace($"Captured WebRTC audio frame: {audioFrame.Data.Length} bytes from {source}");
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
        
        private void AddToBuffer(AudioFrame frame)
        {
            _audioBuffer.Enqueue(frame);
            
            // 检查缓冲区溢出
            if (_audioBuffer.Count > MaxBufferSize)
            {
                // 移除最旧的帧
                var removedCount = 0;
                while (_audioBuffer.Count > MaxBufferSize * 0.8 && _audioBuffer.TryDequeue(out _))
                {
                    removedCount++;
                }
                
                _logger.LogWarning($"Audio buffer overflow, removed {removedCount} old frames");
                BufferOverflow?.Invoke(this, new BufferOverflowEventArgs(removedCount, _audioBuffer.Count));
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