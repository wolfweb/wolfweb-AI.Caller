using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;
using DnsClient;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Threading.Channels;

namespace AI.Caller.Core {
    public sealed partial class AudioBridge : IAudioBridge {
        private const double DtmfGapThresholdMs = 400;
        private const int MinContinuousBlocksForDtmf = 2; // 至少连续 2 个 block 才确认上报，防短噪声

        private readonly ILogger _logger;
        private readonly object _lock = new();
        private readonly object _dtmfLock = new();
        private readonly AudioCodecFactory _codecFactory;
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<int, IAudioCodec> _codecCache = new();
        private readonly Channel<byte[]> _monitoringQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(100) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });


        private bool _isStarted;
        private int _currentBlockSize;

        private int _sameKeyContinuousCount = 0;
        private bool _hasReportedCurrentKey = false;

        private MediaProfile? _profile;
        private IAnalyzer? _dtmfAnalyzer;
        private PhoneKey _currentTrackingKey = PhoneKey.None;
        private StreamingAudioSamples? _streamingAudioSamples;
        private DateTime _lastReportedTime = DateTime.MinValue;
        private PhoneKey _lastConfirmedKey = PhoneKey.None;
        private DateTime _lastConfirmedKeyStopTime = DateTime.MinValue;
        private const double MinGapForSameKeyMs = 80;

        private AudioResampler<byte>? _outputResampler;
        private int _outputResamplerInRate;
        private int _outputResamplerOutRate;

        private AudioResampler<byte>? _dtmfResampler;
        private int _dtmfResamplerInRate;

        private MediaSessionManager? _mediaSessionManager; // 添加MediaSessionManager引用

        public event Action<byte[]>? IncomingAudioReceived;
        public event Action<byte>? OnDtmfToneReceived;

        public AudioBridge(ILogger<AudioBridge> logger, AudioCodecFactory codecFactory) {
            _logger = logger;
            _codecFactory = codecFactory;
            StartMonitoringProcessor();
        }

        /// <summary>
        /// 设置MediaSessionManager引用，用于获取当前协商的编码器
        /// </summary>
        public void SetMediaSessionManager(MediaSessionManager mediaSessionManager) {
            _mediaSessionManager = mediaSessionManager;
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

                _cts.Cancel();
                _monitoringQueue.Writer.TryComplete();

                ResetDtmfState();

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
                byte[] outputPcm = decodedPcm;
                if (expectedSampleRate != currentProfile.SampleRate) {
                    outputPcm = GetOrCreateOutputResampler(expectedSampleRate, currentProfile.SampleRate).Resample(decodedPcm);
                }

                byte[] dtmfPcm = decodedPcm;
                if (expectedSampleRate != 8000) {
                    dtmfPcm = GetOrCreateDtmfResampler(expectedSampleRate).Resample(decodedPcm);                    
                }
                DtmfDetect(dtmfPcm);

                ProcessAudioFrames(outputPcm, currentProfile, frame => {
                    IncomingAudioReceived?.Invoke(frame);
                    // 广播到监听者
                    BroadcastIncomingAudioToMonitors(frame);
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "Error processing incoming audio");
            }
        }

        public void Dispose() {
            Stop();
            lock (_lock) {
                _outputResampler?.Dispose();
                _dtmfResampler?.Dispose();                
                foreach(var codec in _codecCache.Values) {
                    codec.Dispose();
                }
                _codecCache.Clear();
            }

            lock(_dtmfLock){
                _dtmfAnalyzer = null;
                _streamingAudioSamples = null;
            }
            IncomingAudioReceived = null;
        }

        private void DtmfDetect(byte[] decodedPcm) {
            int sampleCount = decodedPcm.Length / 2;
            float[] samples = ArrayPool<float>.Shared.Rent(sampleCount);

            try {
                for (int i = 0; i < sampleCount; i++) {
                    short sample = BitConverter.ToInt16(decodedPcm, i * 2);
                    samples[i] = sample / 32768f;
                }

                lock (_dtmfLock) {                    
                    if (_streamingAudioSamples == null) {
                        _streamingAudioSamples = new StreamingAudioSamples();                        
                    }

                    if(_dtmfAnalyzer == null) {
                        var config = Config.Default;                        
                        var detector = new Detector(1, config);
                        _dtmfAnalyzer = Analyzer.Create(_streamingAudioSamples, detector);
                        _currentBlockSize = config.SampleBlockSize;
                    }

                    _streamingAudioSamples.Write(samples.AsSpan(0, sampleCount));
                    while (_streamingAudioSamples.HasEnoughSamples(_currentBlockSize)) {
                        var dtmfs = _dtmfAnalyzer.AnalyzeNextBlock();
                        var now = DateTime.UtcNow;
                        if (dtmfs != null) {
                            foreach (var change in dtmfs) {
                                if (change.IsStart && change.Key != PhoneKey.None) {
                                    bool isSameKeyBounce = (change.Key == _lastConfirmedKey) && ((now - _lastConfirmedKeyStopTime).TotalMilliseconds < MinGapForSameKeyMs);
                                    if (_currentTrackingKey != change.Key) {
                                        _currentTrackingKey = change.Key;
                                        _sameKeyContinuousCount = 0;
                                        _hasReportedCurrentKey = isSameKeyBounce;

                                        if (isSameKeyBounce) {
                                            _logger.LogDebug("DTMF Bounce detected for key {Key}, merging...", change.Key);
                                        }
                                    }
                                } else if (change.IsStop && change.Key == _currentTrackingKey) {
                                    if (_hasReportedCurrentKey) {
                                        _lastConfirmedKey = _currentTrackingKey;
                                        _lastConfirmedKeyStopTime = now;
                                    }
                                    _currentTrackingKey = PhoneKey.None;
                                    _sameKeyContinuousCount = 0;
                                    _hasReportedCurrentKey = false;
                                }
                            }
                        }

                        if (_currentTrackingKey != PhoneKey.None) {
                            _sameKeyContinuousCount++;

                            double msSinceLastReport = (now - _lastReportedTime).TotalMilliseconds;
                            bool isFirstStable = !_hasReportedCurrentKey && _sameKeyContinuousCount >= MinContinuousBlocksForDtmf;
                            bool isLongPressGap = _hasReportedCurrentKey && msSinceLastReport > DtmfGapThresholdMs;

                            if (isFirstStable || isLongPressGap) {
                                byte tone = _currentTrackingKey.ToByte();

                                _logger.LogInformation("DTMF Confirmed: {Key} (Count: {Count}, IsRepeat: {IsRepeat})", _currentTrackingKey, _sameKeyContinuousCount, _hasReportedCurrentKey);

                                OnDtmfToneReceived?.Invoke(tone);

                                _lastReportedTime = now;
                                _hasReportedCurrentKey = true;

                                _lastConfirmedKey = _currentTrackingKey;
                            }
                        }
                    }
                }
            } finally {
                ArrayPool<float>.Shared.Return(samples);
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

        private AudioResampler<byte> GetOrCreateOutputResampler(int inRate, int outRate) {
            if (_outputResampler != null && (_outputResamplerInRate != inRate || _outputResamplerOutRate != outRate)) {
                _outputResampler.Dispose();
                _outputResampler = null;
            }

            if (_outputResampler == null) {
                _outputResamplerInRate = inRate;
                _outputResamplerOutRate = outRate;
                _outputResampler = new AudioResampler<byte>(inRate, outRate, _logger);
            }
            return _outputResampler;
        }

        private AudioResampler<byte> GetOrCreateDtmfResampler(int inRate) {
            if (_dtmfResampler != null && _dtmfResamplerInRate != inRate) {
                _dtmfResampler.Dispose();
                _dtmfResampler = null;
            }

            if (_dtmfResampler == null) {
                _dtmfResamplerInRate = inRate;
                _dtmfResampler = new AudioResampler<byte>(inRate, 8000, _logger);
            }
            return _dtmfResampler;
        }

        private IAudioCodec? GetCodecForPayloadType(int payloadType) {
            lock (_codecCache) {
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

        private void ProcessOutgoingAudioInternal(byte[] audioFrame) {
            try {
                OutgoingAudioGenerated?.Invoke(audioFrame);

                var currentCodec = GetCurrentNegotiatedCodec();
                var codec = _codecFactory.GetCodec(currentCodec);
                var pcmData = codec.Decode(audioFrame);

                foreach (var listener in _monitoringListeners.Values.Where(l => l.IsActive)) {
                    OutgoingAudioReady?.Invoke(listener.UserId, pcmData);
                    listener.Session?.SendAudio(pcmData, true);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "处理系统播放音频失败");
            }
        }

        private void ResetDtmfState() {
            lock (_dtmfLock) {
                _currentTrackingKey = PhoneKey.None;
                _sameKeyContinuousCount = 0;
                _hasReportedCurrentKey = false;
                _lastReportedTime = DateTime.MinValue;

                _lastConfirmedKey = PhoneKey.None;
                _lastConfirmedKeyStopTime = DateTime.MinValue;
            }
        }

        private void StartMonitoringProcessor() {
            _ = Task.Run(async () => {
                try {
                    await foreach (var audioFrame in _monitoringQueue.Reader.ReadAllAsync(_cts.Token)) {
                        try {
                            ProcessOutgoingAudioInternal(audioFrame);
                        } catch (Exception ex) {
                            _logger.LogError(ex, "监听音频后台处理异常");
                        }
                    }
                } catch (OperationCanceledException) {
                    _logger.LogInformation("监听音频后台处理任务已取消");
                } catch (Exception ex) {
                    _logger.LogError(ex, "监听音频后台处理任务发生未预期的异常");
                }
            }, _cts.Token);
        }
    }
}