using System;
using System.Threading;
using System.Threading.Tasks;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;
using AI.Caller.Core.Media.Sources;
using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core {
    public sealed class AIAutoResponder : IAsyncDisposable {
        private readonly ILogger _logger;
        private readonly ITTSEngine _tts;
        private readonly QueueAudioPlaybackSource _playback;
        private readonly IVoiceActivityDetector _vad;
        private readonly MediaProfile _profile;

        private CancellationTokenSource? _cts;
        private bool _isStarted;

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

        public void OnUplinkPcmFrame(short[] pcm) {
            if (!_isStarted || pcm == null || pcm.Length == 0) return;

            try {
                var result = _vad.Update(pcm);
                if (result.State == VADState.Speaking) {
                    if (!_playback.IsPaused) {
                        _playback.Pause();
                        _logger.LogDebug($"Paused TTS playback - detected speaking (energy: {result.Energy:F4})");
                    }
                } else {
                    if (_playback.IsPaused) {
                        _playback.Resume();
                        _logger.LogDebug($"Resumed TTS playback - silence detected (energy: {result.Energy:F4})");
                    }
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error processing uplink PCM frame");
            }
        }

        public async Task PlayScriptAsync(string text, int speakerId = 0, float speed = 1.0f, CancellationToken ct = default) {
            var token = _cts?.Token ?? ct;
            await foreach (var data in _tts.SynthesizeStreamAsync(text, speakerId, speed)) {
                if (token.IsCancellationRequested) break;

                if (data.FloatData != null && data.FloatData.Length > 0) {
                    EnqueueFloatPcm(data.FloatData);
                }
            }
        }

        private void EnqueueFloatPcm(float[] src) {
            int i = 0;
            int frame = _profile.SamplesPerFrame;
            var tmp = new short[src.Length];
            for (int k = 0; k < src.Length; k++) {
                int v = (int)Math.Round(src[k] * 32767f);
                tmp[k] = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
            }

            while (i < tmp.Length) {
                int len = Math.Min(frame, tmp.Length - i);
                _playback.Enqueue(new ReadOnlySpan<short>(tmp, i, len));
                i += len;
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