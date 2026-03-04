using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Threading.Channels;

namespace AI.Caller.Core {
    public sealed partial class AudioBridge : IAudioBridge {
        private const double DtmfGapThresholdMs = 400;
        private const int MinContinuousBlocksForDtmf = 2; // 至少连续 2 个 block 才确认上报，防短噪声
        private const double MinGapForSameKeyMs = 80;

        private readonly ILogger _logger;
        private readonly object _lock = new();
        private readonly object _dtmfLock = new();
        private readonly AudioCodecFactory _codecFactory;
        private readonly Dictionary<int, IAudioCodec> _codecCache = new();
        private readonly Channel<(byte[], int)> _dtmfQueueWithLength = Channel.CreateUnbounded<(byte[], int)>();
        private readonly Channel<(byte[], int)> _monitoringFrameQueue = Channel.CreateBounded<(byte[], int)>(new BoundedChannelOptions(100) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
        // 1500 frames ≈ 30 seconds of audio at 20ms/frame, DropOldest ensures bounded memory
        private readonly Channel<byte[]> _monitoringPcmQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1500) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
        private bool _enableAsyncMonitoring = true;

        private Task? _dtmfProcessorTask;
        private Task? _monitoringFrameProcessorTask;
        private Task? _monitoringPcmProcessorTask;
        private readonly CancellationTokenSource _processorCts = new();

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

        private AudioResampler<byte>? _outputResampler;
        private int _outputResamplerInRate;
        private int _outputResamplerOutRate;

        private int _dtmfResamplerInRate;
        private AudioResampler<byte>? _dtmfResampler;

        // 监听输出专用重采样器（与 _dtmfResampler 隔离，避免线程竞争）
        private int _monitoringOutResamplerInRate;
        private AudioResampler<byte>? _monitoringOutResampler;
        private MediaSessionManager? _mediaSessionManager; // 添加MediaSessionManager引用

        public event Action<byte[]>? IncomingAudioReceived;
        public event Action<byte>? OnDtmfToneReceived;

        public AudioBridge(ILogger<AudioBridge> logger, AudioCodecFactory codecFactory) {
            _logger = logger;
            _codecFactory = codecFactory;
            StartTaskProcessor();
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

        /// <summary>
        /// Process raw PCM audio from AIAutoResponder for monitoring (skips decode step)
        /// </summary>
        public void ProcessOutgoingPcm(byte[] pcmData) {
            // Unconditionally buffer audio to ensure playhead continuity for late joiners
            _monitoringPcmQueue.Writer.TryWrite(pcmData);
        }

        public void Stop() {
            lock (_lock) {
                if (!_isStarted) return;

                _isStarted = false;

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

            byte[] decodedPcm = ArrayPool<byte>.Shared.Rent(audioData.Length * 2);
            try {
                _logger.LogTrace("🎵 AudioBridge processing: PayloadType={PayloadType}, InputSampleRate={InputSampleRate}, ProfileSampleRate={ProfileSampleRate}, Size={Size} bytes", payloadType, sampleRate, currentProfile.SampleRate, audioData.Length);

                var codec = GetCodecForPayloadType(payloadType);
                if (codec == null) {
                    _logger.LogWarning("Unsupported payload type: {PayloadType}", payloadType);
                    return;
                }

                var decodedLength = codec.Decode(audioData, decodedPcm);
                _logger.LogTrace("🎵 Decoded PCM: {DecodedSize} bytes", decodedLength);
                
                // 创建精确大小的临时数组用于重采样
                byte[] validPcmData = ArrayPool<byte>.Shared.Rent(decodedLength);
                try {
                    Array.Copy(decodedPcm, 0, validPcmData, 0, decodedLength);

                    int expectedSampleRate = GetDecodedSampleRate(payloadType);
                    ArraySegment<byte> outputPcm;
                    if (expectedSampleRate != currentProfile.SampleRate) {
                        outputPcm = GetOrCreateOutputResampler(expectedSampleRate, currentProfile.SampleRate).Resample(validPcmData);
                    } else {
                        outputPcm = new ArraySegment<byte>(validPcmData, 0, decodedLength);
                    }

                    ArraySegment<byte> dtmfPcm;
                    if (expectedSampleRate != 8000) {
                        dtmfPcm = GetOrCreateDtmfResampler(expectedSampleRate).Resample(validPcmData);
                    } else {
                        dtmfPcm = new ArraySegment<byte>(validPcmData, 0, decodedLength);
                    }
                    
                    if (dtmfPcm.Array != null && dtmfPcm.Count > 0) {
                        byte[] dtmfCopy = ArrayPool<byte>.Shared.Rent(dtmfPcm.Count);
                        Array.Copy(dtmfPcm.Array, dtmfPcm.Offset, dtmfCopy, 0, dtmfPcm.Count);
                        if (!_dtmfQueueWithLength.Writer.TryWrite((dtmfCopy, dtmfPcm.Count))) {
                            ArrayPool<byte>.Shared.Return(dtmfCopy);
                        }
                    }

                    ProcessAudioFrames(outputPcm, currentProfile, frame => {
                        IncomingAudioReceived?.Invoke(frame);
                        
                        if (_enableAsyncMonitoring) {
                            byte[] frameCopy = ArrayPool<byte>.Shared.Rent(frame.Length);
                            Array.Copy(frame, 0, frameCopy, 0, frame.Length);
                            if (!_monitoringFrameQueue.Writer.TryWrite((frameCopy, frame.Length))) {
                                ArrayPool<byte>.Shared.Return(frameCopy);
                            }
                        } else {
                             BroadcastIncomingAudioToMonitors(frame);
                        }
                    });
                } finally {
                    ArrayPool<byte>.Shared.Return(validPcmData);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error processing incoming audio");
            } finally {
                ArrayPool<byte>.Shared.Return(decodedPcm);
            }             
        }

        private bool _disposed = false;

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            
            Stop();
            
            if (!_processorCts.IsCancellationRequested) {
                _processorCts.Cancel();
            }
            
            try {
                Task.WaitAll(new[] { _dtmfProcessorTask, _monitoringFrameProcessorTask, _monitoringPcmProcessorTask }
                    .Where(t => t != null).ToArray()!, TimeSpan.FromSeconds(5));
            } catch (AggregateException ex) {
                _logger.LogWarning(ex, "等待处理器任务完成时发生异常");
            }
            
            CleanupChannelArrays();
            
            lock (_lock) {
                _outputResampler?.Dispose();
                _dtmfResampler?.Dispose();
                _monitoringOutResampler?.Dispose();                
                foreach(var codec in _codecCache.Values) {
                    codec.Dispose();
                }
                _codecCache.Clear();
            }

            _monitoringFrameQueue.Writer.Complete();
            _monitoringPcmQueue.Writer.Complete();
            _dtmfQueueWithLength.Writer.Complete();

            lock (_dtmfLock){
                _dtmfAnalyzer = null;
                _streamingAudioSamples = null;
            }
            
            try {
                _processorCts.Dispose();
            } catch (ObjectDisposedException) {
            }
            
            IncomingAudioReceived = null;
            OnDtmfToneReceived = null;
            IncomingAudioReady = null;
            OutgoingAudioReady = null;
            OutgoingAudioGenerated = null;
            InterventionAudioSend = null;
            
            _mediaSessionManager = null;
        }

        /// <summary>
        /// 清理 Channel 中未消费的 ArrayPool 数组，防止内存泄漏
        /// </summary>
        private void CleanupChannelArrays() {
            while (_dtmfQueueWithLength.Reader.TryRead(out var dtmfItem)) {
                ArrayPool<byte>.Shared.Return(dtmfItem.Item1);
            }
            
            while (_monitoringFrameQueue.Reader.TryRead(out var frameItem)) {
                ArrayPool<byte>.Shared.Return(frameItem.Item1);
            }
            
            _logger.LogDebug("已清理 Channel 中未消费的 ArrayPool 数组");
        }

        private void DtmfDetect(byte[] decodedPcm, int length) {
            int sampleCount = length / 2;
            float[] samples = ArrayPool<float>.Shared.Rent(sampleCount);

            try {
                for (int i = 0; i < sampleCount; i++) {
                    short sample = BitConverter.ToInt16(decodedPcm, i * 2);
                    samples[i] = sample / 32768f;
                }

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

                            _logger.LogInformation("DTMF Confirmed: {Key} (Count: {Count}, IsRepeat: {IsRepeat})",
                                _currentTrackingKey, _sameKeyContinuousCount, _hasReportedCurrentKey);

                            // 检查是否处于人工介入状态
                            if (!_isInterventionActive) {
                                OnDtmfToneReceived?.Invoke(tone);
                            } else {
                                _logger.LogDebug("人工介入期间忽略DTMF按键: {Key}", _currentTrackingKey);
                            }

                            _lastReportedTime = now;
                            _hasReportedCurrentKey = true;

                            _lastConfirmedKey = _currentTrackingKey;
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

        /// <summary>
        /// 监听输出专用重采样器（线程隔离，仅由 _monitoringPcmProcessorTask 调用）
        /// </summary>
        private AudioResampler<byte> GetOrCreateMonitoringOutResampler(int inRate) {
            if (_monitoringOutResampler != null && _monitoringOutResamplerInRate != inRate) {
                _monitoringOutResampler.Dispose();
                _monitoringOutResampler = null;
            }

            if (_monitoringOutResampler == null) {
                _monitoringOutResamplerInRate = inRate;
                _monitoringOutResampler = new AudioResampler<byte>(inRate, 8000, _logger);
            }
            return _monitoringOutResampler;
        }

        private IAudioCodec? GetCodecForPayloadType(int payloadType) {
            if (_codecCache.TryGetValue(payloadType, out var codec)) {
                return codec;
            }
            lock (_codecCache) {
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
        
        private void ProcessAudioFrames(ArraySegment<byte> audioData, MediaProfile profile, Action<byte[]> frameProcessor) {
            if (profile == null || audioData.Array == null) return;

            int frameBytes = profile.SamplesPerFrame * 2;
            int offset = 0;
            int dataLength = audioData.Count;

            byte[] frame = ArrayPool<byte>.Shared.Rent(frameBytes);
            try {
                while (offset < dataLength) {
                    int remainingBytes = dataLength - offset;
                    int currentFrameBytes = Math.Min(frameBytes, remainingBytes);

                    Array.Copy(audioData.Array, audioData.Offset + offset, frame, 0, currentFrameBytes);

                    if (currentFrameBytes < frameBytes) {
                        Array.Clear(frame, currentFrameBytes, frameBytes - currentFrameBytes);
                    }

                    frameProcessor(frame);
                    offset += currentFrameBytes;
                }
            } finally {
                ArrayPool<byte>.Shared.Return(frame);
            }
        }

        private void ResetDtmfState() {
            _currentTrackingKey = PhoneKey.None;
            _sameKeyContinuousCount = 0;
            _hasReportedCurrentKey = false;
            _lastReportedTime = DateTime.MinValue;

            _lastConfirmedKey = PhoneKey.None;
            _lastConfirmedKeyStopTime = DateTime.MinValue;
        }

        private void StartTaskProcessor() {

            var cancellationToken = _processorCts.Token;

            _dtmfProcessorTask = Task.Run(async () => {
                await foreach (var (item, length) in _dtmfQueueWithLength.Reader.ReadAllAsync(cancellationToken)) {
                    try {
                        DtmfDetect(item, length);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "DTMF检测异常");
                    } finally {
                        ArrayPool<byte>.Shared.Return(item);
                    }
                }
            }, cancellationToken);

            _monitoringFrameProcessorTask = Task.Run(async () => {
                 await foreach (var (frame, length) in _monitoringFrameQueue.Reader.ReadAllAsync(cancellationToken)) {
                     try {
                         byte[] frameData = new byte[length];
                         Array.Copy(frame, 0, frameData, 0, length);
                         BroadcastIncomingAudioToMonitors(frameData);
                     } catch (Exception ex) {
                         _logger.LogError(ex, "Incoming monitoring async processing error");
                     } finally {
                         ArrayPool<byte>.Shared.Return(frame);
                     }
                 }
            }, cancellationToken);

            _monitoringPcmProcessorTask = Task.Run(async () => {
                Thread.CurrentThread.Priority = ThreadPriority.Normal;
                
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
                
                byte[]? currentStashedFrame = null;
                int currentStashedOffset = 0;

                int sampleRate = _profile?.SampleRate ?? 8000;
                int bytesPerFrame = (sampleRate * 2 * 20) / 1000;
                byte[] buffer = new byte[bytesPerFrame];

                try {
                    while (await timer.WaitForNextTickAsync(cancellationToken)) {
                        try {
                            int currentSampleRate = _profile?.SampleRate ?? 8000;
                            if (currentSampleRate != sampleRate) {
                                sampleRate = currentSampleRate;
                                bytesPerFrame = (sampleRate * 2 * 20) / 1000;
                                buffer = new byte[bytesPerFrame];
                            }
                            
                            Array.Clear(buffer, 0, bytesPerFrame);
                            int bytesWritten = 0;
                            
                            while (bytesWritten < bytesPerFrame) {
                                if (currentStashedFrame == null) {
                                    if (_monitoringPcmQueue.Reader.TryRead(out var newFrame)) {
                                        currentStashedFrame = newFrame;
                                        currentStashedOffset = 0;
                                    } else {
                                        break; 
                                    }
                                }
                                
                                int bytesNeeded = bytesPerFrame - bytesWritten;
                                int bytesAvailable = currentStashedFrame.Length - currentStashedOffset;
                                int bytesToCopy = Math.Min(bytesNeeded, bytesAvailable);
                                
                                Array.Copy(currentStashedFrame, currentStashedOffset, buffer, bytesWritten, bytesToCopy);
                                
                                bytesWritten += bytesToCopy;
                                currentStashedOffset += bytesToCopy;
                                
                                if (currentStashedOffset >= currentStashedFrame.Length) {
                                    currentStashedFrame = null;
                                    currentStashedOffset = 0;
                                }
                            }
                            
                            BroadcastOutgoingPcmToMonitors(buffer);
                            
                        } catch (Exception ex) {
                            _logger.LogError(ex, "PCM monitoring pacing loop error");
                        }
                    }
                } catch (OperationCanceledException) {
                    // Normal cancellation
                }
            }, cancellationToken);
        }        
    }
}