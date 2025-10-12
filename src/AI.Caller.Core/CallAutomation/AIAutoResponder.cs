using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Channels;
using System.Buffers;
using System.Collections.Concurrent;

namespace AI.Caller.Core {
    public sealed class AIAutoResponder : IAsyncDisposable {
        private readonly ILogger _logger;
        private readonly ITTSEngine _tts;
        private readonly MediaProfile _profile;
        private readonly G711Codec _g711Codec;
        private readonly byte[] _g711SilenceFrame;
        private readonly IVoiceActivityDetector _vad;
        private readonly Channel<byte[]> _jitterBuffer;
        private readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;        
        private readonly ConcurrentDictionary<int, AudioResamplerCrossType<float, byte>> _resamplerCache = new();
        private readonly Stopwatch _performanceStopwatch = new();
        private readonly object _audioBufferLock = new object();
        
        private bool _isStarted;
        private Task? _playoutTask;
        private long _totalBytesSent;
        private byte[]? _lastSentFrame;
        private int _emptyFrameCount = 0;
        private long _totalBytesGenerated;
        private CancellationTokenSource? _cts;
        private byte[] _audioBuffer = Array.Empty<byte>();
        private DateTime _lastVadStateChange = DateTime.MinValue;
        private TaskCompletionSource? _playbackCompletionSource;

        private const int JitterBufferWaterline = 300;
        private const int LowWatermark = 100;
        private const int VadDebounceMs = 100;

        public event Action<byte[]>? OutgoingAudioGenerated;

        private volatile bool _shouldSendAudio = true;
        private volatile bool _isTtsStreamFinished;
        private volatile bool _shouldStopPlayout = false;

        public AIAutoResponder(
            ILogger logger,
            ITTSEngine tts,
            IVoiceActivityDetector vad,
            MediaProfile profile,
            G711Codec g711Codec) {
            _tts = tts;
            _vad = vad;
            _logger = logger;
            _profile = profile;
            _g711Codec = g711Codec;

            _jitterBuffer = Channel.CreateUnbounded<byte[]>();

            var silentPcmFrame = new byte[_profile.SamplesPerFrame * 2];
            if (_profile.Codec == AudioCodec.PCMU) {
                _g711SilenceFrame = _g711Codec.EncodeMuLaw(silentPcmFrame);
            } else {
                _g711SilenceFrame = _g711Codec.EncodeALaw(silentPcmFrame);
            }
        }

        public Task StartAsync(CancellationToken ct = default) {
            if (_isStarted) {
                _logger.LogWarning("AIAutoResponder is already started");
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _isStarted = true;
            _audioBuffer = Array.Empty<byte>();
            _shouldSendAudio = true;

            _playoutTask = Task.Run(() => PlayoutLoop(_cts.Token));

            _logger.LogInformation("AIAutoResponder started successfully with Jitter Buffer Playout Loop.");
            return Task.CompletedTask;
        }

        public void OnUplinkPcmFrame(byte[] pcmBytes) {
            if (!_isStarted || pcmBytes == null || pcmBytes.Length == 0) return;

            try {
                var result = _vad.Update(pcmBytes);
                bool isSpeaking = result.State == VADState.Speaking;

                var now = DateTime.UtcNow;
                if (isSpeaking && _shouldSendAudio && (now - _lastVadStateChange).TotalMilliseconds >= VadDebounceMs) {
                    _shouldSendAudio = false;
                    _lastVadStateChange = now;
                    _logger.LogDebug($"VAD: Detected speaking, pausing TTS audio playout. (Energy: {result.Energy:F4})");
                } else if (!isSpeaking && !_shouldSendAudio && (now - _lastVadStateChange).TotalMilliseconds >= VadDebounceMs) {
                    _shouldSendAudio = true;
                    _lastVadStateChange = now;
                    _logger.LogDebug($"VAD: Detected silence, resuming TTS audio playout. (Energy: {result.Energy:F4})");
                }
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

        public async Task<TimeSpan> PlayScriptAsync(string text, int speakerId = 0, float speed = 1.0f, CancellationToken ct = default) {
            Interlocked.Exchange(ref _totalBytesGenerated, 0);
            Interlocked.Exchange(ref _totalBytesSent, 0);
            _playbackCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _isTtsStreamFinished = false;

            var token = _cts?.Token ?? ct;
            var ttsGenerationStopwatch = Stopwatch.StartNew();
            var stopwatch = Stopwatch.StartNew();
            int chunkCount = 0;
            int preBufferChunks = 3;
            var preBuffer = new List<float[]>();
            var preBufferSampleRates = new List<int>();
            await foreach (var data in _tts.SynthesizeStreamAsync(text, speakerId, speed).WithCancellation(token)) {
                if (token.IsCancellationRequested) break;

                if (data.FloatData != null && data.FloatData.Length > 0) {
                    chunkCount++;
                    if (chunkCount <= preBufferChunks) {
                        preBuffer.Add(data.FloatData);
                        preBufferSampleRates.Add(data.SampleRate);
                    } else {
                        foreach (var (floatData, sampleRate) in preBuffer.Zip(preBufferSampleRates)) {
                            ProcessTtsAudioChunk(floatData, sampleRate);
                        }
                        preBuffer.Clear();
                        preBufferSampleRates.Clear();
                        ProcessTtsAudioChunk(data.FloatData, data.SampleRate);
                    }

                    _logger.LogTrace($"Processed TTS chunk {chunkCount} in {stopwatch.ElapsedMilliseconds} ms.");
                    stopwatch.Restart();
                }
            }

            foreach (var (floatData, sampleRate) in preBuffer.Zip(preBufferSampleRates)) {
                ProcessTtsAudioChunk(floatData, sampleRate);
            }

            if (!token.IsCancellationRequested) {
                Flush();
                _logger.LogInformation($"Finished processing TTS script with {chunkCount} chunks.");
            }
            _isTtsStreamFinished = true;
            
            ttsGenerationStopwatch.Stop();
            _logger.LogInformation($"TTS generation completed in {ttsGenerationStopwatch.ElapsedMilliseconds}ms");
            return ttsGenerationStopwatch.Elapsed;
        }

        private void Flush() {
            int frameSizeInBytes = _profile.SamplesPerFrame * 2;
            if (_audioBuffer.Length > 0) {
                var finalFrame = new byte[frameSizeInBytes];
                Array.Copy(_audioBuffer, finalFrame, Math.Min(_audioBuffer.Length, frameSizeInBytes));

                byte[] payload;
                if (_profile.Codec == AudioCodec.PCMU) {
                    payload = _g711Codec.EncodeMuLaw(finalFrame.AsSpan());
                } else {
                    payload = _g711Codec.EncodeALaw(finalFrame.AsSpan());
                }

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

                Parallel.For(0, frameCount, new ParallelOptions { 
                    MaxDegreeOfParallelism = Environment.ProcessorCount / 2 
                }, i => {
                    int offset = i * frameSizeInBytes;                    
                    var pcmFrame = new ReadOnlySpan<byte>(combinedBuffer, offset, frameSizeInBytes);
                    
                    byte[] payload;
                    lock (_g711Codec) {
                        payload = _profile.Codec == AudioCodec.PCMU
                            ? _g711Codec.EncodeMuLaw(pcmFrame)
                            : _g711Codec.EncodeALaw(pcmFrame);
                    }
                    
                    encodedFrames[i] = payload;
                });

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

                while (!ct.IsCancellationRequested && !_shouldStopPlayout) {
                    while (_jitterBuffer.Reader.Count < JitterBufferWaterline && !ct.IsCancellationRequested && !_shouldStopPlayout) {
                        int initialCount = _jitterBuffer.Reader.Count;
                        await Task.Delay(100, ct);
                        
                        if (_shouldStopPlayout) {
                            _logger.LogInformation("Playout stop signal received, exiting loop.");
                            break;
                        }
                        
                        if (_jitterBuffer.Reader.Count > 0 && _isTtsStreamFinished) {
                            _logger.LogInformation("TTS finished with {Count} frames in buffer, starting playout.", _jitterBuffer.Reader.Count);
                            break;
                        }
                        
                        if (_jitterBuffer.Reader.Count == 0) {
                            continue;
                        }
                    }
                    
                    if (_shouldStopPlayout) {
                        _logger.LogInformation("Playout stop signal detected, terminating loop.");
                        break;
                    }

                    if (_jitterBuffer.Reader.Count > 0) {
                        _logger.LogInformation("Jitter buffer ready ({Count} frames). Starting playout.", _jitterBuffer.Reader.Count);
                    }

                    while (!ct.IsCancellationRequested) {
                        long loopStartTime = stopwatch.ElapsedMilliseconds;

                        if (_jitterBuffer.Reader.Count < LowWatermark && !_isTtsStreamFinished) {
                            int retryCount = 0;
                            while (_jitterBuffer.Reader.Count < JitterBufferWaterline && !ct.IsCancellationRequested && retryCount++ < 5) {
                                int delay = 50 + retryCount * 50;
                                await Task.Delay(delay, ct);
                                _logger.LogWarning("Jitter buffer low ({Count} frames), pausing {Delay}ms to re-buffer...", _jitterBuffer.Reader.Count, delay);
                            }
                            if (_jitterBuffer.Reader.Count >= JitterBufferWaterline) {
                                _logger.LogInformation("Jitter buffer recovered to {Count} frames, resuming playout.", _jitterBuffer.Reader.Count);
                            } else {
                                _logger.LogWarning("Jitter buffer still low ({Count} frames), continuing with caution.", _jitterBuffer.Reader.Count);
                            }
                        }

                        byte[]? frameToSend = await GetNextFrameOptimized(ct);
                        if (frameToSend == null) {
                            _logger.LogInformation("Segment playback complete: Sent {Sent} >= Generated {Generated} bytes.", 
                                Interlocked.Read(ref _totalBytesSent), Interlocked.Read(ref _totalBytesGenerated));                            
                            _playbackCompletionSource?.TrySetResult();                            
                            break;
                        }

                        OutgoingAudioGenerated?.Invoke(frameToSend);
                        double adaptiveDelay = CalculateAdaptiveDelay();
                        smoothedDelayMs = 0.3 * adaptiveDelay + 0.7 * smoothedDelayMs;
                        smoothedDelayMs = Math.Clamp(smoothedDelayMs, _profile.PtimeMs * 0.95, _profile.PtimeMs * 1.05);

                        long elapsedInLoop = stopwatch.ElapsedMilliseconds - loopStartTime;
                        long adjustedDelay = (long)(smoothedDelayMs - elapsedInLoop);
                        if (adjustedDelay > 0) {
                            await Task.Delay((int)adjustedDelay, ct);
                        }
                        _logger.LogTrace($"Sending frame, buffer status: {_jitterBuffer.Reader.Count} frames, delay: {smoothedDelayMs:F2}ms");
                    }
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
            if (/*_shouldSendAudio*/true) {
                if (_jitterBuffer.Reader.TryRead(out var audioFrame)) {
                    _emptyFrameCount = 0;
                    _lastSentFrame = audioFrame;
                    Interlocked.Add(ref _totalBytesSent, audioFrame.Length);
                    return audioFrame;
                } else {
                    if (_isTtsStreamFinished && _jitterBuffer.Reader.Count == 0 && 
                        Interlocked.Read(ref _totalBytesSent) >= Interlocked.Read(ref _totalBytesGenerated)) {
                        return null;
                    }
                    
                    _emptyFrameCount++;
                    
                    if (_emptyFrameCount == 1 && !_isTtsStreamFinished) {
                        await Task.Delay(2, ct);
                        if (_jitterBuffer.Reader.TryRead(out audioFrame)) {
                            _emptyFrameCount = 0;
                            _lastSentFrame = audioFrame;
                            Interlocked.Add(ref _totalBytesSent, audioFrame.Length);
                            return audioFrame;
                        }
                    }
                    
                    if (_emptyFrameCount == 1) {
                        _logger.LogTrace("First empty frame, repeating last frame");
                        return _lastSentFrame ?? _g711SilenceFrame;
                    } else {
                        _logger.LogTrace("Multiple empty frames, sending silence");
                        return _g711SilenceFrame;
                    }
                }
            } else {
                return _g711SilenceFrame;
            }
        }

        private double CalculateAdaptiveDelay() {
            int bufferCount = _jitterBuffer.Reader.Count;
            
            if (bufferCount == 0) {
                return _profile.PtimeMs * 1.02;
            } else if (bufferCount < LowWatermark) {
                return _profile.PtimeMs * 1.01;
            } else if (bufferCount > JitterBufferWaterline) {
                return _profile.PtimeMs * 0.99;
            }
            
            return _profile.PtimeMs;
        }

        public async ValueTask DisposeAsync() {
            await StopAsync();
            _cts?.Dispose();
            
            foreach (var resampler in _resamplerCache.Values) {
                resampler?.Dispose();
            }
            _resamplerCache.Clear();
            
            OutgoingAudioGenerated = null;
        }
    }
}