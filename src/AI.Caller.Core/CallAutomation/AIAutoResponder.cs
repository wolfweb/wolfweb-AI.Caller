using AI.Caller.Core.CallAutomation;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Media.Interfaces;
using AI.Caller.Core.Media.Vad;
using AI.Caller.Core.Models;
using AI.Caller.Core.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace AI.Caller.Core {
    public sealed partial class AIAutoResponder : IAsyncDisposable {
        private readonly record struct TtsCacheKey(string Text, int SpeakerId, float Speed);

        private readonly ILogger _logger;
        private readonly ITTSEngine _tts;
        private readonly IFrameTimer _frameTimer;
        private readonly IMemoryCache _memoryCache;
        private readonly IDtmfService? _dtmfService;
        private readonly ILoggerFactory _loggerFactory;
        private readonly Channel<byte[]> _jitterBuffer;
        private readonly AudioCodecFactory _codecFactory;
        private readonly object _audioBufferLock = new object();
        private readonly Stopwatch _performanceStopwatch = new();
        private readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;        
        private readonly ConcurrentDictionary<int, AudioResamplerCrossType<float, byte>> _resamplerCache = new();
        
        private TaskCompletionSource? _playbackCompletionSource;
        private CancellationTokenSource? _cts;
        private IAudioCodec _currentCodec;
        private byte[] _silenceFrame;
        private MediaProfile _profile;
        private Task? _playoutTask;
        
        private bool _isStarted;
        private string? _currentCallId;
        private long _totalBytesSent;
        private byte[]? _lastSentFrame;
        private int _emptyFrameCount = 0;
        private long _totalBytesGenerated;
        private byte[] _audioBuffer = Array.Empty<byte>();

        private const int JitterBufferWaterline = 200;

        public event Action<byte[]>? OutgoingAudioGenerated;

        private volatile bool _shouldSendAudio = true;
        private volatile bool _isTtsStreamFinished;
        private volatile bool _shouldStopPlayout = false;

        public AIAutoResponder(
            ILoggerFactory loggerFactory,
            ITTSEngine tts,
            MediaProfile profile,
            AudioCodecFactory codecFactory,
            IDtmfService? dtmfService = null) {
            _tts = tts;
            _logger = loggerFactory.CreateLogger<AIAutoResponder>();
            _profile = profile;
            _dtmfService = dtmfService;
            _codecFactory = codecFactory;
            _loggerFactory = loggerFactory;
            _memoryCache = new MemoryCache(new MemoryCacheOptions());

            _jitterBuffer = Channel.CreateUnbounded<byte[]>();
            _currentCodec = _codecFactory.GetCodec(_profile.Codec);
            _frameTimer = new HighPrecisionFrameTimer(_profile.PtimeMs);
            _silenceFrame = _currentCodec.GenerateSilenceFrame(_profile.PtimeMs);
        }

        public Task StartAsync(CancellationToken ct = default) {
            if (_isStarted) {
                _logger.LogWarning("AIAutoResponder is already started");
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _isStarted = true;
            // _audioBuffer = Array.Empty<byte>(); // Removed to preserve pre-generated audio
            _shouldSendAudio = true;

            _logger.LogInformation("AIAutoResponder started. Jitter Buffer Count: {Count}", _jitterBuffer.Reader.Count);

            _playoutTask = Task.Run(() => PlayoutLoop(_cts.Token));

            return Task.CompletedTask;
        }

        public void OnUplinkPcmFrame(byte[] pcmBytes) {
            if (!_isStarted || pcmBytes == null || pcmBytes.Length == 0) return;

            try {
                _shouldSendAudio = true;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error processing uplink PCM frame");
            }
        }

        public bool ShouldSendAudio => _shouldSendAudio;

        public Task WaitForPlaybackToCompleteAsync() {
            return _playbackCompletionSource?.Task ?? Task.CompletedTask;
        }

        public void SignalPlayoutComplete() {
            _shouldStopPlayout = true;
            _logger.LogInformation("Playout completion signal received.");
        }

        /// <summary>
        /// Update media profile when codec configuration changes
        /// </summary>
        public void UpdateMediaProfile(AudioCodec codec, int sampleRate, int payloadType) {
            try {
                // 🔧 FIX: 使用工厂方法创建协商后的配置
                var newProfile = MediaProfile.FromNegotiation(
                    codec: codec,
                    payloadType: payloadType,
                    sampleRate: sampleRate,
                    ptimeMs: _profile.PtimeMs,
                    channels: _profile.Channels
                );

                if (_profile.Codec != codec) {
                    _currentCodec = _codecFactory.GetCodec(codec);
                    _silenceFrame = _currentCodec.GenerateSilenceFrame(newProfile.PtimeMs);                    
                    _logger.LogInformation("Updated codec from {OldCodec} to {NewCodec}", _profile.Codec, codec);
                }

                _profile = newProfile;
                _logger.LogInformation("Media profile updated: {Codec}@{SampleRate}Hz (PT:{PayloadType})", 
                    codec, sampleRate, payloadType);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to update media profile");
            }
        }

        public async Task<TimeSpan> PlayScriptAsync(string text, int speakerId = 0, float speed = 1.0f, CancellationToken ct = default) {
            _isTtsStreamFinished = false;
            Interlocked.Exchange(ref _totalBytesGenerated, 0);
            Interlocked.Exchange(ref _totalBytesSent, 0);
            Interlocked.Exchange(ref _playbackCompletionSource, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            var token = ct != default ? ct : (_cts?.Token ?? CancellationToken.None);
            var cacheKey = new TtsCacheKey(text, speakerId, speed);

            if (_memoryCache.TryGetValue(cacheKey, out List<AudioData>? cachedAudio)) {
                var replayStopwatch = Stopwatch.StartNew();
                foreach (var data in cachedAudio) {
                    if (data.FloatData != null && data.FloatData.Length > 0) {
                        ProcessTtsAudioChunk(data.FloatData, data.SampleRate);
                    }
                }
                Flush();
                _isTtsStreamFinished = true;

                replayStopwatch.Stop();
                _logger.LogDebug("TTS cache replay completed in {ElapsedMs}ms", replayStopwatch.ElapsedMilliseconds);
                return replayStopwatch.Elapsed;
            }

            var ttsGenerationStopwatch = Stopwatch.StartNew();
            var stopwatch = Stopwatch.StartNew();
            var generatedAudio = new List<AudioData>();

            await foreach (var data in _tts.SynthesizeStreamAsync(text, speakerId, speed).WithCancellation(token)) {
                if (token.IsCancellationRequested) break;

                if (data.FloatData != null && data.FloatData.Length > 0) {
                    generatedAudio.Add(data);
                    ProcessTtsAudioChunk(data.FloatData, data.SampleRate);
                    stopwatch.Restart();
                }
            }

            if (!token.IsCancellationRequested) {
                Flush();
            }
            _isTtsStreamFinished = true;

            if (generatedAudio.Count > 0) {
                var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(30));
                _memoryCache.Set(cacheKey, generatedAudio, cacheEntryOptions);
                _logger.LogDebug("TTS result cached, chunks={Count}, key hash={Hash}", generatedAudio.Count, cacheKey.GetHashCode());
            }

            ttsGenerationStopwatch.Stop();
            _logger.LogInformation($"TTS generation completed in {ttsGenerationStopwatch.ElapsedMilliseconds}ms");
            return ttsGenerationStopwatch.Elapsed;
        }

        private void Flush() {
            int frameSizeInBytes = _profile.SamplesPerFrame * 2;
            if (_audioBuffer.Length > 0) {
                var finalFrame = new byte[frameSizeInBytes];
                Array.Copy(_audioBuffer, finalFrame, Math.Min(_audioBuffer.Length, frameSizeInBytes));

                byte[] payload = _currentCodec.Encode(finalFrame.AsSpan());

                Interlocked.Add(ref _totalBytesGenerated, payload.Length);
                if (!_jitterBuffer.Writer.TryWrite(payload)) {
                    _logger.LogWarning("Failed to write flushed frame to jitter buffer.");
                }
                _logger.LogTrace($"Flushed final {_audioBuffer.Length} bytes of audio, padded to {frameSizeInBytes} bytes.");

                _audioBuffer = Array.Empty<byte>();
            }
        }

        private void ProcessTtsAudioChunk(float[] src, int ttsSampleRate) {
            _performanceStopwatch.Restart();
            
            byte[] pcmBytes;
            int pcmLength;
            
            var resampler = _resamplerCache.GetOrAdd(ttsSampleRate, rate => {
                _logger.LogInformation("Creating cached cross-type AudioResampler for {InputRate} -> {OutputRate} Hz.", rate, _profile.SampleRate);
                return new AudioResamplerCrossType<float, byte>(rate, _profile.SampleRate, _logger);
            });
            
            lock (resampler) {
                var resampledBytes = resampler.Resample(src);
                pcmLength = resampledBytes.Length;
                pcmBytes = _bytePool.Rent(pcmLength);
                Array.Copy(resampledBytes, pcmBytes, pcmLength);
            }
            
            try {
                ProcessAudioFramesSafe(pcmBytes, pcmLength);
            } finally {
                _bytePool.Return(pcmBytes);
            }

            var processingTime = _performanceStopwatch.ElapsedMilliseconds;
            if (processingTime > _profile.PtimeMs * 0.8) {
                _logger.LogWarning("Audio processing taking {ProcessingTime}ms, target is {TargetTime}ms", 
                    processingTime, _profile.PtimeMs);
            }

            _logger.LogDebug($"Jitter buffer status: {_jitterBuffer.Reader.Count} frames");
        }

        private void ProcessAudioFramesSafe(byte[] newPcmBytes, int length) {
            byte[] localAudioBuffer;
            int localBufferLength;
            
            lock (_audioBufferLock) {
                localAudioBuffer = _audioBuffer;
                localBufferLength = _audioBuffer.Length;
            }
                
            var totalLength = localBufferLength + length;
            var combinedBuffer = _bytePool.Rent(totalLength);
            
            try {                
                if (localBufferLength > 0) {
                    Buffer.BlockCopy(localAudioBuffer, 0, combinedBuffer, 0, localBufferLength);
                }
                Buffer.BlockCopy(newPcmBytes, 0, combinedBuffer, localBufferLength, length);

                int frameSizeInBytes = _profile.SamplesPerFrame * 2;
                int frameCount = totalLength / frameSizeInBytes;                
                var encodedFrames = new byte[frameCount][];

                if(_currentCodec is G722Codec) {
                    for (int i = 0; i < frameCount; i++) {
                        int offset = i * frameSizeInBytes;
                        var pcmFrame = new ReadOnlySpan<byte>(combinedBuffer, offset, frameSizeInBytes);
                        byte[] payload;
                        lock (_currentCodec) {
                            payload = _currentCodec.Encode(pcmFrame);
                        }
                        encodedFrames[i] = payload;
                    }
                } else {
                    Parallel.For(0, frameCount, new ParallelOptions {
                        MaxDegreeOfParallelism = Environment.ProcessorCount / 2
                    }, i => {
                        int offset = i * frameSizeInBytes;
                        var pcmFrame = new ReadOnlySpan<byte>(combinedBuffer, offset, frameSizeInBytes);

                        byte[] payload;
                        lock (_currentCodec) {
                            payload = _currentCodec.Encode(pcmFrame);
                        }

                        encodedFrames[i] = payload;
                    });
                }

                for (int i = 0; i < frameCount; i++) {
                    var payload = encodedFrames[i];
                    
                    if (payload == null || payload.Length == 0) {
                        throw new InvalidOperationException($"Frame {i} encoding failed, payload is empty");
                    }
                    
                    Interlocked.Add(ref _totalBytesGenerated, payload.Length);
                    
                    if (!_jitterBuffer.Writer.TryWrite(payload)) {
                        throw new InvalidOperationException($"Failed to write frame {i} to jitter buffer. Channel may be closed.");
                    }
                }

                lock (_audioBufferLock) {
                    int remainingBytes = totalLength - (frameCount * frameSizeInBytes);
                    if (remainingBytes > 0) {
                        _audioBuffer = new byte[remainingBytes];
                        Buffer.BlockCopy(combinedBuffer, frameCount * frameSizeInBytes, _audioBuffer, 0, remainingBytes);
                    } else {
                        _audioBuffer = Array.Empty<byte>();
                    }
                }
                
            } finally {
                _bytePool.Return(combinedBuffer);
            }
        }

        private async Task PlayoutLoop(CancellationToken ct) {
            _logger.LogInformation("Playout loop started. Running continuously until stopped.");

            try {
                var stopwatch = Stopwatch.StartNew();
                double smoothedDelayMs = _profile.PtimeMs;

                while (_jitterBuffer.Reader.Count < JitterBufferWaterline && !ct.IsCancellationRequested && !_shouldStopPlayout && !_isTtsStreamFinished) {
                    await Task.Delay(50, ct);
                }

                _frameTimer.Reset();
                while (!ct.IsCancellationRequested && !_shouldStopPlayout) {
                    long loopStartTime = stopwatch.ElapsedMilliseconds;

                    byte[]? frameToSend = await GetNextFrameOptimized(ct);
                    if (frameToSend == null) {
                        OutgoingAudioGenerated?.Invoke(_silenceFrame); //发送静音帧以保持连接, 同时录音需要记录完整
                        _logger.LogInformation("Segment playback complete: Sent {Sent} >= Generated {Generated} bytes.", Interlocked.Read(ref _totalBytesSent), Interlocked.Read(ref _totalBytesGenerated));
                    } else {
                        OutgoingAudioGenerated?.Invoke(frameToSend);
                    }

                    await _frameTimer.WaitForNextFrameAsync(ct);
                    _logger.LogTrace($"Sending frame, buffer status: {_jitterBuffer.Reader.Count} frames, delay: {smoothedDelayMs:F2}ms");
                }
            } catch (OperationCanceledException) {
                _logger.LogInformation("Playout loop was cancelled.");
            } catch (Exception ex) {
                _logger.LogError(ex, "Critical error in playout loop.");
            } finally {
                _logger.LogInformation("Playout loop terminated.");
                _playbackCompletionSource?.TrySetResult();
            }
        }

        public async Task StopAsync() {
            if (!_isStarted) return;
            _isStarted = false;

            _logger.LogInformation("Stopping AIAutoResponder...");

            _jitterBuffer.Writer.TryComplete();

            if (_cts != null) {
                try {
                    if (!_cts.IsCancellationRequested) {
                        _cts.Cancel();
                    }
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Exception during cancellation token cancellation.");
                }
            }

            if (_playoutTask != null) {
                try {
                    await _playoutTask;
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Exception while waiting for playout task to complete.");
                }
            }

            _logger.LogInformation("AIAutoResponder stopped.");
        }

        private async Task<byte[]?> GetNextFrameOptimized(CancellationToken ct) {
            if (_shouldSendAudio) {
                if (_jitterBuffer.Reader.TryRead(out var audioFrame)) {
                    _emptyFrameCount = 0;
                    _lastSentFrame = audioFrame;
                    Interlocked.Add(ref _totalBytesSent, audioFrame.Length);
                    return audioFrame;
                } else {
                    if (_isTtsStreamFinished && _jitterBuffer.Reader.Count == 0 && Interlocked.Read(ref _totalBytesSent) >= Interlocked.Read(ref _totalBytesGenerated)) {
                        await Task.Delay(100); //等待sdp发送音频后再结束

                        bool isStillFinished = _isTtsStreamFinished
                                      && _jitterBuffer.Reader.Count == 0
                                      && Interlocked.Read(ref _totalBytesSent) >= Interlocked.Read(ref _totalBytesGenerated);

                        if (isStillFinished) {
                            _playbackCompletionSource?.TrySetResult();
                            return null;
                        }
                        return null;
                    }
                    
                    _emptyFrameCount++;

                    if (_emptyFrameCount == 1 && !_isTtsStreamFinished) {
                        if (_jitterBuffer.Reader.TryRead(out audioFrame)) {
                            _emptyFrameCount = 0;
                            _lastSentFrame = audioFrame;
                            Interlocked.Add(ref _totalBytesSent, audioFrame.Length);
                            return audioFrame;
                        }
                    }
                    
                    if (_emptyFrameCount == 1) {
                        _logger.LogTrace("First empty frame, repeating last frame");
                        return _lastSentFrame ?? _silenceFrame;
                    } else {
                        _logger.LogTrace("Multiple empty frames, sending silence");
                        return _silenceFrame;
                    }
                }
            } else {
                return _silenceFrame;
            }
        }

        // DTMF 相关方法
        
        /// <summary>
        /// 设置当前通话上下文
        /// </summary>
        public void SetCallContext(string callId) {
            _currentCallId = callId;
            _logger.LogDebug("CallContext已设置: {CallId}", callId);
        }

        /// <summary>
        /// 收集DTMF输入
        /// </summary>
        public async Task<string> CollectDtmfInputAsync(int maxLength, char terminationKey = '#', char backspaceKey = '*', TimeSpan? timeout = null, CancellationToken ct = default) {            
            if (_dtmfService == null) {
                _logger.LogError("DtmfService未设置，无法收集DTMF输入");
                throw new InvalidOperationException("DtmfService未设置");
            }

            if (string.IsNullOrEmpty(_currentCallId)) {
                _logger.LogError("CallId未设置，无法收集DTMF输入");
                throw new InvalidOperationException("CallId未设置");
            }

            _logger.LogInformation("开始收集DTMF输入，最大长度: {MaxLength}, CallId: {CallId}", maxLength, _currentCallId);

            try {
                var config = new DtmfCollectionConfig {
                    MaxLength = maxLength,
                    TerminationKey = terminationKey,
                    BackspaceKey = backspaceKey,
                    Timeout = timeout,
                    EnableLogging = true,
                    Description = $"AI场景DTMF收集 - CallId: {_currentCallId}"
                };

                var input = await _dtmfService.StartCollectionWithConfigAsync(_currentCallId, config, ct);
                _logger.LogInformation("DTMF输入收集完成: {CallId}", _currentCallId);
                return input;
            } catch (TimeoutException) {
                _logger.LogWarning("DTMF输入超时: {CallId}", _currentCallId);
                throw;
            } catch (Exception ex) {
                _logger.LogError(ex, "DTMF输入收集失败: {CallId}", _currentCallId);
                throw;
            }
        }

        /// <summary>
        /// 处理DTMF按键（由SIPClient调用）
        /// </summary>
        public void OnDtmfToneReceived(byte tone) {
            if (_dtmfService != null && !string.IsNullOrEmpty(_currentCallId)) {
                _dtmfService.OnDtmfToneReceived(_currentCallId, tone);
            } else {
                _logger.LogWarning("无法处理DTMF按键：DtmfService或CallId未设置");
            }
        }

        public async ValueTask DisposeAsync() {
            await StopAsync();
            _cts?.Dispose();
            
            foreach (var resampler in _resamplerCache.Values) {
                resampler?.Dispose();
            }
            _resamplerCache.Clear();
            
            OutgoingAudioGenerated = null;
            _memoryCache.Dispose();
        }
    }
}