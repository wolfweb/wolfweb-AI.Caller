using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;
using AI.Caller.Core.Media.Sources;
using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using static vpxmd.VpxCodecCxPkt;

namespace AI.Caller.Core {
    public sealed class AIAutoResponder : IAsyncDisposable {
        private readonly ILogger _logger;
        private readonly ITTSEngine _tts;
        private readonly QueueAudioPlaybackSource _playback;
        private readonly IVoiceActivityDetector _vad;
        private readonly MediaProfile _profile;

        private CancellationTokenSource? _cts;
        private bool _isStarted;
        private volatile bool _shouldSendAudio = true; // 控制是否应该发送音频到RTP

        public AIAutoResponder(ILogger logger, ITTSEngine tts, QueueAudioPlaybackSource playback, IVoiceActivityDetector vad, MediaProfile profile) {
            _tts = tts;
            _vad = vad;
            _logger = logger;
            _profile = profile;
            _playback = playback;
        }

        public async Task StartAsync(CancellationToken ct = default) {
            if (_isStarted) {
                _logger.LogWarning("AIAutoResponder is already started");
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _playback.Init(_profile);
            await _playback.StartAsync(_cts.Token);
            _isStarted = true;

            _logger.LogInformation("AIAutoResponder started successfully");
        }

        public void OnUplinkPcmFrame(byte[] pcmBytes) {
            if (!_isStarted || pcmBytes == null || pcmBytes.Length == 0) return;

            try {
                var result = _vad.Update(pcmBytes);
                
                if (result.State == VADState.Speaking) {
                    if (!_playback.IsPaused) {
                        _playback.Pause();
                        _logger.LogDebug($"Paused TTS playback - detected speaking (energy: {result.Energy:F4})");
                    }
                    _shouldSendAudio = false; 
                } else {
                    if (_playback.IsPaused) {
                        _playback.Resume();
                        _logger.LogDebug($"Resumed TTS playback - silence detected (energy: {result.Energy:F4})");
                    }
                    _shouldSendAudio = true;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error processing uplink PCM frame");
            }
        }

        public bool ShouldSendAudio => _shouldSendAudio;

        public async Task PlayScriptAsync(string text, int speakerId = 0, float speed = 1.0f, CancellationToken ct = default) {
            var token = _cts?.Token ?? ct;
            await foreach (var data in _tts.SynthesizeStreamAsync(text, speakerId, speed)) {
                if (token.IsCancellationRequested) break;

                if (data.FloatData != null && data.FloatData.Length > 0) {
                    EnqueueFloatPcm(data.FloatData, data.SampleRate);
                }
            }
        }

        private void EnqueueFloatPcm(float[] src, int ttsSampleRate) {
            int frameBytes = _profile.SamplesPerFrame * 2;

            float[] processedFloat = src;
            if (src.Length > 0 && ttsSampleRate != _profile.SampleRate) {
                using var resampler = new AudioResampler<float>(
                    ttsSampleRate, 
                    _profile.SampleRate, 
                    _logger);
                processedFloat = resampler.Resample(src);
                _logger.LogDebug($"Resampled audio from {ttsSampleRate}Hz to {_profile.SampleRate}Hz");
            }

            var shortSamples = new short[processedFloat.Length];
            for (int i = 0; i < processedFloat.Length; i++) {
                float sample = processedFloat[i];
                if (float.IsNaN(sample) || float.IsInfinity(sample)) {
                    sample = 0f;
                } else {
                    sample = Math.Clamp(sample, -1f, 1f);
                }
                
                int intSample = (int)MathF.Round(sample * 32767f);
                shortSamples[i] = (short)Math.Clamp(intSample, short.MinValue, short.MaxValue);
            }

            var byteArray = new byte[shortSamples.Length * 2];
            for (int i = 0; i < shortSamples.Length; i++) {
                short sample = shortSamples[i];
                byteArray[i * 2] = (byte)(sample & 0xFF);        // 低字节
                byteArray[i * 2 + 1] = (byte)((sample >> 8) & 0xFF); // 高字节
            }

            int offset = 0;
            while (offset < byteArray.Length) {
                int len = Math.Min(frameBytes, byteArray.Length - offset);
                _playback.Enqueue(new ReadOnlySpan<byte>(byteArray, offset, len));
                offset += len;
            }
        }

        public async Task StopAsync() {
            if (!_isStarted) return;

            if (_cts != null) {
                try { _cts.Cancel(); } catch { }
            }
            await _playback.StopAsync();
            _isStarted = false;

            _logger.LogInformation("AIAutoResponder stopped");
        }

        public async ValueTask DisposeAsync() {
            try { await StopAsync(); } catch { }
            await _playback.DisposeAsync();
            _cts?.Dispose();
        }
    }
}