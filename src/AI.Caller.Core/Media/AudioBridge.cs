using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core {
    public sealed partial class AudioBridge : IAudioBridge {
        private readonly ILogger _logger;
        private readonly object _lock = new();
        private readonly AudioCodecFactory _codecFactory;
        private readonly Dictionary<int, IAudioCodec> _codecCache = new();
        
        private MediaProfile? _profile;
        private bool _isStarted;
        private MediaSessionManager? _mediaSessionManager; // 添加MediaSessionManager引用

        public event Action<byte[]>? IncomingAudioReceived;

        public AudioBridge(ILogger<AudioBridge> logger, AudioCodecFactory codecFactory) {
            _logger = logger;
            _codecFactory = codecFactory;
        }

        /// <summary>
        /// 设置MediaSessionManager引用，用于获取当前协商的编码器
        /// </summary>
        public void SetMediaSessionManager(MediaSessionManager mediaSessionManager) {
            _mediaSessionManager = mediaSessionManager;
            _logger.LogDebug("MediaSessionManager reference set for AudioBridge");
        }

        public void Initialize(MediaProfile profile) {
            lock (_lock) {
                _profile = profile;
                _logger.LogDebug($"AudioBridge initialized with profile: SampleRate={profile.SampleRate}, SamplesPerFrame={profile.SamplesPerFrame}");
            }
        }

        public void Start() {
            lock (_lock) {
                if (_isStarted) return;

                if (_profile == null) {
                    throw new InvalidOperationException("AudioBridge must be initialized before starting");
                }

                _isStarted = true;
                _logger.LogInformation("AudioBridge started");
            }
        }

        public void Stop() {
            lock (_lock) {
                if (!_isStarted) return;

                _isStarted = false;

                _logger.LogInformation("AudioBridge stopped");
            }
        }

        public void ProcessIncomingAudio(byte[] audioData, int sampleRate, int payloadType) {
            MediaProfile? currentProfile;
            bool isStarted;
            
            lock (_lock) {
                isStarted = _isStarted;
                currentProfile = _profile;
            }
            
            if (!isStarted || currentProfile == null) return;

            try {
                _logger.LogTrace("🎵 AudioBridge processing: PayloadType={PayloadType}, InputSampleRate={InputSampleRate}, ProfileSampleRate={ProfileSampleRate}, Size={Size} bytes", payloadType, sampleRate, currentProfile.SampleRate, audioData.Length);

                var codec = GetCodecForPayloadType(payloadType);
                if (codec == null) {
                    _logger.LogWarning("Unsupported payload type: {PayloadType}", payloadType);
                    return;
                }

                byte[] decodedPcm = codec.Decode(audioData);
                _logger.LogTrace("🎵 Decoded PCM: {DecodedSize} bytes", decodedPcm.Length);
                
                int expectedSampleRate = GetDecodedSampleRate(payloadType);
                
                if (expectedSampleRate != currentProfile.SampleRate) {
                    _logger.LogWarning("MediaProfile configuration mismatch: codec expects {ExpectedSampleRate}Hz but profile is {ProfileSampleRate}Hz. This indicates MediaConfigurationChanged event may not have updated the profile correctly.", expectedSampleRate, currentProfile.SampleRate);                    
                    using var resampler = new AudioResampler<byte>(expectedSampleRate, currentProfile.SampleRate, _logger);
                    decodedPcm = resampler.Resample(decodedPcm);
                }

                ProcessAudioFrames(decodedPcm, currentProfile, frame => {
                    IncomingAudioReceived?.Invoke(frame);
                    // 广播到监听者
                    BroadcastIncomingAudioToMonitors(frame);
                });

            } catch (Exception ex) {
                _logger.LogError(ex, "Error processing incoming audio");
            }
        }

        private void ProcessAudioFrames(byte[] audioData, MediaProfile profile, Action<byte[]> frameProcessor) {
            if (profile == null) return;

            int frameBytes = profile.SamplesPerFrame * 2;
            int offset = 0;

            while (offset < audioData.Length) {
                int remainingBytes = audioData.Length - offset;
                int currentFrameBytes = Math.Min(frameBytes, remainingBytes);

                var frame = new byte[frameBytes];
                Array.Copy(audioData, offset, frame, 0, currentFrameBytes);

                if (currentFrameBytes < frameBytes) {
                    for (int i = currentFrameBytes; i < frameBytes; i++) {
                        frame[i] = 0;
                    }
                }

                frameProcessor(frame);
                offset += currentFrameBytes;
            }
        }

        public void Dispose() {
            Stop();
            IncomingAudioReceived = null;
        }

        private IAudioCodec? GetCodecForPayloadType(int payloadType) {
            if (_codecCache.TryGetValue(payloadType, out var codec)) {
                return codec;
            }

            AudioCodec codecType = payloadType switch {
                0 => AudioCodec.PCMU,
                8 => AudioCodec.PCMA,
                9 => AudioCodec.G722,
                _ => AudioCodec.PCMA // Default fallback
            };

            try {
                codec = _codecFactory.GetCodec(codecType);
                _codecCache[payloadType] = codec;
                _logger.LogDebug($"Created codec for payload type {payloadType}: {codecType}");
                return codec;
            } catch (Exception ex) {
                _logger.LogError(ex, $"Failed to create codec for payload type {payloadType}");
                return null;
            }
        }

        /// <summary>
        /// 获取解码后 PCM 数据的采样率
        /// </summary>
        /// <param name="payloadType">负载类型</param>
        /// <returns>解码后的采样率</returns>
        private int GetDecodedSampleRate(int payloadType) {
            return payloadType switch {
                0 => 8000,  // PCMU -> 8kHz PCM
                8 => 8000,  // PCMA -> 8kHz PCM  
                9 => 16000, // G722 -> 16kHz PCM
                _ => 8000   // Default
            };
        }
    }
}