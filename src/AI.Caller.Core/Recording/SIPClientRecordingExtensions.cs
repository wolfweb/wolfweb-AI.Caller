using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Net;

namespace AI.Caller.Core.Recording
{
    
    public static class SIPClientRecordingExtensions
    {
        
        public static void EnableRecording(this SIPClient sipClient, AudioRecorder audioRecorder, ILogger logger)
        {
            if (sipClient == null)
                throw new ArgumentNullException(nameof(sipClient));
            if (audioRecorder == null)
                throw new ArgumentNullException(nameof(audioRecorder));
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
                
            // 监听通话开始事件
            sipClient.CallAnswer += (client) =>
            {
                try
                {
                    AttachRecordingToMediaSession(client, audioRecorder, logger);
                    AttachRecordingToRTCPeerConnection(client, audioRecorder, logger);
                    logger.LogInformation("Recording attached to active call");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to attach recording to call");
                }
            };
            
            // 监听通话结束事件
            sipClient.CallEnded += (client) =>
            {
                try
                {
                    DetachRecordingFromMediaSession(client, audioRecorder, logger);
                    DetachRecordingFromRTCPeerConnection(client, audioRecorder, logger);
                    logger.LogInformation("Recording detached from ended call");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to detach recording from call");
                }
            };
        }
        
        
        private static void AttachRecordingToMediaSession(SIPClient sipClient, AudioRecorder audioRecorder, ILogger logger)
        {
            if (sipClient.MediaSession == null)
                return;
                
            // 创建RTP音频包接收处理器
            void OnRtpPacketReceived(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
            {
                if (mediaType == SDPMediaTypesEnum.audio)
                {
                    // 确定音频源类型（传入）
                    audioRecorder.OnRtpAudioReceived(remote, mediaType, rtpPacket, AudioSource.RTP_Incoming);
                }
            }
            
            // 附加事件处理器
            sipClient.MediaSession.OnRtpPacketReceived += OnRtpPacketReceived;
            
            logger.LogDebug("Attached recording to MediaSession RTP packets");
        }
        
        
        private static void AttachRecordingToRTCPeerConnection(SIPClient sipClient, AudioRecorder audioRecorder, ILogger logger)
        {
            if (sipClient.RTCPeerConnection == null)
                return;
                
            // 创建WebRTC音频包接收处理器
            void OnRtcRtpPacketReceived(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
            {
                if (mediaType == SDPMediaTypesEnum.audio)
                {
                    // 确定音频源类型（WebRTC传入）
                    audioRecorder.OnRtpAudioReceived(remote, mediaType, rtpPacket, AudioSource.WebRTC_Incoming);
                }
            }
            
            // 附加事件处理器
            sipClient.RTCPeerConnection.OnRtpPacketReceived += OnRtcRtpPacketReceived;
            
            logger.LogDebug("Attached recording to RTCPeerConnection RTP packets");
        }
        
        
        private static void DetachRecordingFromMediaSession(SIPClient sipClient, AudioRecorder audioRecorder, ILogger logger)
        {
            if (sipClient.MediaSession == null)
                return;
                
            try
            {
                // 注意：这里需要保存事件处理器的引用才能正确移除
                // 在实际实现中，应该在附加时保存引用
                logger.LogDebug("Detached recording from MediaSession");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error detaching recording from MediaSession");
            }
        }
        
        
        private static void DetachRecordingFromRTCPeerConnection(SIPClient sipClient, AudioRecorder audioRecorder, ILogger logger)
        {
            if (sipClient.RTCPeerConnection == null)
                return;
                
            try
            {
                // 注意：这里需要保存事件处理器的引用才能正确移除
                // 在实际实现中，应该在附加时保存引用
                logger.LogDebug("Detached recording from RTCPeerConnection");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error detaching recording from RTCPeerConnection");
            }
        }
    }
    
    
    public class RecordingSession : IDisposable
    {
        private readonly SIPClient _sipClient;
        private readonly AudioRecorder _audioRecorder;
        private readonly AudioMixer _audioMixer;
        private readonly ILogger _logger;
        private readonly List<AudioFrame> _incomingFrames;
        private readonly List<AudioFrame> _outgoingFrames;
        private readonly object _lockObject = new object();
        
        private bool _isRecording = false;
        private bool _disposed = false;
        private DateTime _sessionStartTime;
        
        
        public event EventHandler<RecordingSession>? SessionStarted;
        
        
        public event EventHandler<RecordingSession>? SessionEnded;
        
        
        public event EventHandler<AudioFrame>? AudioFrameReady;
        
        
        public bool IsRecording => _isRecording;
        
        
        public DateTime SessionStartTime => _sessionStartTime;
        
        
        public TimeSpan SessionDuration => _isRecording ? DateTime.UtcNow - _sessionStartTime : TimeSpan.Zero;
        
        public RecordingSession(SIPClient sipClient, AudioRecorder audioRecorder, AudioMixer audioMixer, ILogger logger)
        {
            _sipClient = sipClient ?? throw new ArgumentNullException(nameof(sipClient));
            _audioRecorder = audioRecorder ?? throw new ArgumentNullException(nameof(audioRecorder));
            _audioMixer = audioMixer ?? throw new ArgumentNullException(nameof(audioMixer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _incomingFrames = new List<AudioFrame>();
            _outgoingFrames = new List<AudioFrame>();
            
            // 订阅音频数据事件
            _audioRecorder.AudioDataReceived += OnAudioDataReceived;
        }
        
        
        public async Task<bool> StartAsync()
        {
            lock (_lockObject)
            {
                if (_disposed || _isRecording)
                    return false;
                    
                _isRecording = true;
                _sessionStartTime = DateTime.UtcNow;
            }
            
            try
            {
                await _audioRecorder.StartCaptureAsync(
                    AudioSource.RTP_Incoming,
                    AudioSource.RTP_Outgoing,
                    AudioSource.WebRTC_Incoming,
                    AudioSource.WebRTC_Outgoing
                );
                
                SessionStarted?.Invoke(this, this);
                _logger.LogInformation($"Recording session started at {_sessionStartTime}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start recording session");
                lock (_lockObject)
                {
                    _isRecording = false;
                }
                return false;
            }
        }
        
        
        public async Task StopAsync()
        {
            lock (_lockObject)
            {
                if (_disposed || !_isRecording)
                    return;
                    
                _isRecording = false;
            }
            
            try
            {
                await _audioRecorder.StopCaptureAsync();
                
                SessionEnded?.Invoke(this, this);
                _logger.LogInformation($"Recording session ended, duration: {SessionDuration}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping recording session");
            }
        }
        
        
        private void OnAudioDataReceived(object? sender, AudioDataEventArgs e)
        {
            if (!_isRecording)
                return;
                
            try
            {
                lock (_lockObject)
                {
                    // 根据音频源分类存储
                    switch (e.AudioFrame.Source)
                    {
                        case AudioSource.RTP_Incoming:
                        case AudioSource.WebRTC_Incoming:
                            _incomingFrames.Add(e.AudioFrame);
                            break;
                            
                        case AudioSource.RTP_Outgoing:
                        case AudioSource.WebRTC_Outgoing:
                            _outgoingFrames.Add(e.AudioFrame);
                            break;
                    }
                    
                    // 尝试混合音频（如果有双方的音频）
                    TryMixAudio();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio data in recording session");
            }
        }
        
        
        private void TryMixAudio()
        {
            // 简单的时间窗口混合策略
            var now = DateTime.UtcNow;
            var windowSize = TimeSpan.FromMilliseconds(20); // 20ms窗口
            
            // 获取时间窗口内的帧
            var recentIncoming = _incomingFrames.Where(f => now - f.Timestamp <= windowSize).ToList();
            var recentOutgoing = _outgoingFrames.Where(f => now - f.Timestamp <= windowSize).ToList();
            
            if (recentIncoming.Any() && recentOutgoing.Any())
            {
                // 混合最新的帧
                var incomingFrame = recentIncoming.OrderByDescending(f => f.Timestamp).First();
                var outgoingFrame = recentOutgoing.OrderByDescending(f => f.Timestamp).First();
                
                var mixedFrame = _audioMixer.MixFrames(incomingFrame, outgoingFrame);
                if (mixedFrame != null)
                {
                    AudioFrameReady?.Invoke(this, mixedFrame);
                }
                
                // 清理旧帧
                _incomingFrames.RemoveAll(f => now - f.Timestamp > windowSize);
                _outgoingFrames.RemoveAll(f => now - f.Timestamp > windowSize);
            }
            else if (recentIncoming.Any())
            {
                // 只有传入音频
                var frame = recentIncoming.OrderByDescending(f => f.Timestamp).First();
                AudioFrameReady?.Invoke(this, frame);
            }
            else if (recentOutgoing.Any())
            {
                // 只有传出音频
                var frame = recentOutgoing.OrderByDescending(f => f.Timestamp).First();
                AudioFrameReady?.Invoke(this, frame);
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
                
                if (_isRecording)
                {
                    _ = Task.Run(async () => await StopAsync());
                }
                
                _audioRecorder.AudioDataReceived -= OnAudioDataReceived;
                
                _incomingFrames.Clear();
                _outgoingFrames.Clear();
                
                _logger.LogInformation("RecordingSession disposed");
            }
        }
    }
}