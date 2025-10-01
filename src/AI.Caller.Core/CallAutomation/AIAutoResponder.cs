using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Channels;

namespace AI.Caller.Core {
    public sealed class AIAutoResponder : IAsyncDisposable {
        private readonly ILogger _logger;
        private readonly ITTSEngine _tts;
        private readonly IVoiceActivityDetector _vad;
        private readonly MediaProfile _profile;
        private readonly G711Codec _g711Codec;

        private AudioResampler<float>? _resampler;
        private int _currentTtsSampleRate;
        private byte[] _audioBuffer = Array.Empty<byte>();

        private readonly Channel<byte[]> _jitterBuffer;
        private Task? _playoutTask;
        private readonly byte[] _g711SilenceFrame;
        private const int JitterBufferWaterline = 200;
        private const int LowWatermark = 20;
        private const int MaxConsecutiveSilenceFrames = 10;
        private int _consecutiveSilenceFrames = 0;

        public event Action<byte[]>? OutgoingAudioGenerated;
        private CancellationTokenSource? _cts;
        private bool _isStarted;
        private volatile bool _shouldSendAudio = true;
        private DateTime _lastVadStateChange = DateTime.MinValue;
        private const int VadDebounceMs = 100;

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
            _consecutiveSilenceFrames = 0;

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

        public async Task PlayScriptAsync(string text, int speakerId = 0, float speed = 1.0f, CancellationToken ct = default) {
            var token = _cts?.Token ?? ct;
            var stopwatch = Stopwatch.StartNew();
            int chunkCount = 0;
            await foreach (var data in _tts.SynthesizeStreamAsync(text, speakerId, speed).WithCancellation(token)) {
                if (token.IsCancellationRequested) break;

                if (data.FloatData != null && data.FloatData.Length > 0) {
                    chunkCount++;
                    ProcessTtsAudioChunk(data.FloatData, data.SampleRate);
                    _logger.LogTrace($"Processed TTS chunk {chunkCount} in {stopwatch.ElapsedMilliseconds} ms.");
                    stopwatch.Restart();
                }
            }

            if (!token.IsCancellationRequested) {
                Flush();
                _logger.LogInformation($"Finished processing TTS script with {chunkCount} chunks.");
            }
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

                if (!_jitterBuffer.Writer.TryWrite(payload)) {
                    _logger.LogWarning("Failed to write flushed frame to jitter buffer.");
                }
                _logger.LogTrace($"Flushed final {_audioBuffer.Length} bytes of audio, padded to {frameSizeInBytes} bytes.");

                _audioBuffer = Array.Empty<byte>();
            }
        }

        private void ProcessTtsAudioChunk(float[] src, int ttsSampleRate) {
            float[] processedFloat = src;
            if (src.Length > 0 && ttsSampleRate != _profile.SampleRate) {
                if (_resampler == null || _currentTtsSampleRate != ttsSampleRate) {
                    _resampler?.Dispose();
                    _resampler = new AudioResampler<float>(ttsSampleRate, _profile.SampleRate, _logger);
                    _currentTtsSampleRate = ttsSampleRate;
                    _logger.LogInformation("Initialized AudioResampler for {InputRate} -> {OutputRate} Hz.", ttsSampleRate, _profile.SampleRate);
                }
                processedFloat = _resampler.Resample(src);
            }

            var shortSamples = new short[processedFloat.Length];
            for (int i = 0; i < processedFloat.Length; i++) {
                float sample = Math.Clamp(processedFloat[i], -1f, 1f);
                shortSamples[i] = (short)(sample * 32767f);
            }

            var pcmBytes = new byte[shortSamples.Length * 2];
            Buffer.BlockCopy(shortSamples, 0, pcmBytes, 0, pcmBytes.Length);

            var combinedBuffer = new byte[_audioBuffer.Length + pcmBytes.Length];
            Buffer.BlockCopy(_audioBuffer, 0, combinedBuffer, 0, _audioBuffer.Length);
            Buffer.BlockCopy(pcmBytes, 0, combinedBuffer, _audioBuffer.Length, pcmBytes.Length);

            int frameSizeInBytes = _profile.SamplesPerFrame * 2;
            int offset = 0;
            while (offset + frameSizeInBytes <= combinedBuffer.Length) {
                var pcmFrame = new Span<byte>(combinedBuffer, offset, frameSizeInBytes);

                byte[] payload;
                if (_profile.Codec == AudioCodec.PCMU) {
                    payload = _g711Codec.EncodeMuLaw(pcmFrame);
                } else {
                    payload = _g711Codec.EncodeALaw(pcmFrame);
                }

                if (!_jitterBuffer.Writer.TryWrite(payload)) {
                    _logger.LogWarning("Failed to write frame to jitter buffer. Channel may be closed.");
                    break;
                }

                offset += frameSizeInBytes;
            }

            int remainingBytes = combinedBuffer.Length - offset;
            if (remainingBytes > 0) {
                _audioBuffer = new byte[remainingBytes];
                Buffer.BlockCopy(combinedBuffer, offset, _audioBuffer, 0, remainingBytes);
            } else {
                _audioBuffer = Array.Empty<byte>();
            }

            _logger.LogDebug($"Jitter buffer status: {_jitterBuffer.Reader.Count} frames");
        }

        private async Task PlayoutLoop(CancellationToken ct) {
            _logger.LogInformation("Playout loop started. Waiting for jitter buffer to reach waterline ({Waterline} frames)...", JitterBufferWaterline);

            try {
                while (_jitterBuffer.Reader.Count < JitterBufferWaterline && !ct.IsCancellationRequested) {
                    await _jitterBuffer.Reader.WaitToReadAsync(ct);
                }

                _logger.LogInformation("Jitter buffer waterline reached. Starting high-precision playout.");

                var stopwatch = Stopwatch.StartNew();

                while (!ct.IsCancellationRequested) {
                    long loopStartTime = stopwatch.ElapsedMilliseconds;

                    if (_jitterBuffer.Reader.Count < LowWatermark) {
                        _logger.LogWarning("Jitter buffer low ({Count} frames), pausing playout...", _jitterBuffer.Reader.Count);
                        while (_jitterBuffer.Reader.Count < JitterBufferWaterline && !ct.IsCancellationRequested) {
                            await _jitterBuffer.Reader.WaitToReadAsync(ct);
                        }
                        _logger.LogInformation("Jitter buffer recovered to {Count} frames, resuming playout.", _jitterBuffer.Reader.Count);
                        _consecutiveSilenceFrames = 0; 
                    }

                    byte[] frameToSend;
                    if (_shouldSendAudio) {
                        if (_jitterBuffer.Reader.TryRead(out var audioFrame)) {
                            frameToSend = audioFrame;
                            _consecutiveSilenceFrames = 0;
                        } else {
                            _consecutiveSilenceFrames++;
                            if (_consecutiveSilenceFrames > MaxConsecutiveSilenceFrames) {
                                _logger.LogWarning("Too many consecutive silence frames ({Count}), pausing playout...", _consecutiveSilenceFrames);
                                while (_jitterBuffer.Reader.Count < JitterBufferWaterline && !ct.IsCancellationRequested) {
                                    await _jitterBuffer.Reader.WaitToReadAsync(ct);
                                }
                                _consecutiveSilenceFrames = 0;
                                _logger.LogInformation("Jitter buffer recovered to {Count} frames, resuming playout.", _jitterBuffer.Reader.Count);
                            }
                            frameToSend = _g711SilenceFrame;
                            _logger.LogTrace("Jitter buffer empty, sending silent frame to maintain continuity.");
                        }
                    } else {
                        frameToSend = _g711SilenceFrame;
                        _consecutiveSilenceFrames = 0;
                    }

                    OutgoingAudioGenerated?.Invoke(frameToSend);

                    long elapsedInLoop = stopwatch.ElapsedMilliseconds - loopStartTime;
                    long delayMs = _profile.PtimeMs - elapsedInLoop;

                    if (_jitterBuffer.Reader.Count < LowWatermark * 2) {
                        delayMs = (long)(delayMs * 1.2);
                        _logger.LogDebug($"Low buffer ({_jitterBuffer.Reader.Count} frames), increasing delay to {delayMs} ms.");
                    }

                    if (delayMs > 0) {
                        await Task.Delay((int)delayMs, ct);
                    }

                    if (stopwatch.ElapsedMilliseconds % 1000 < 10)
                    {
                        _logger.LogDebug($"Jitter buffer status: {_jitterBuffer.Reader.Count} frames, consecutive silence frames: {_consecutiveSilenceFrames}");
                    }
                }
            } catch (OperationCanceledException) {
                _logger.LogInformation("Playout loop was cancelled.");
            } catch (Exception ex) {
                _logger.LogError(ex, "Critical error in playout loop.");
            } finally {
                _logger.LogInformation("Playout loop terminated.");
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

        public async ValueTask DisposeAsync() {
            await StopAsync();
            _cts?.Dispose();
            _resampler?.Dispose();
            OutgoingAudioGenerated = null;
        }
    }
}