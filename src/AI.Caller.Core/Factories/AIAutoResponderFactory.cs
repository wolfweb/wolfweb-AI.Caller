using AI.Caller.Core.Interfaces;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Sources;
using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using AI.Caller.Core.Media.Encoders;
using System.Collections.Concurrent;

namespace AI.Caller.Core {
    public class AIAutoResponderFactory : IAIAutoResponderFactory {
        private readonly ILogger<AIAutoResponder> _logger;
        private readonly ITTSEngine _ttsEngine;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<AIAutoResponder, CancellationTokenSource> _senderLoops = new();

        public AIAutoResponderFactory(
            ILogger<AIAutoResponder> logger,
            ITTSEngine ttsEngine,
            IServiceProvider serviceProvider) {
            _logger = logger;
            _ttsEngine = ttsEngine;
            _serviceProvider = serviceProvider;
        }

        public AIAutoResponder CreateAutoResponder(IAudioBridge audioBridge, MediaProfile profile) {
            var playbackSource = new QueueAudioPlaybackSource();

            var vad = new EnergyVad();
            vad.Configure(
                energyThreshold: 0.02f,
                enterSpeakingMs: 200,
                resumeSilenceMs: 600,
                sampleRate: profile.SampleRate,
                frameMs: profile.PtimeMs
            );

            var autoResponder = new AIAutoResponder(_logger, _ttsEngine, playbackSource, vad, profile);

            ConnectAudioBridge(autoResponder, audioBridge, playbackSource, vad);

            return autoResponder;
        }

        public AIAutoResponder CreateWithRtp(IAudioBridge audioBridge, MediaProfile profile, RTPSession rtpSession) {
            var playbackSource = new QueueAudioPlaybackSource();

            var vad = new EnergyVad();
            vad.Configure(
                energyThreshold: 0.02f,
                enterSpeakingMs: 200,
                resumeSilenceMs: 600,
                sampleRate: profile.SampleRate,
                frameMs: profile.PtimeMs
            );

            var autoResponder = new AIAutoResponder(_logger, _ttsEngine, playbackSource, vad, profile);

            ConnectAudioBridge(autoResponder, audioBridge, playbackSource, vad);

            var cts = new CancellationTokenSource();
            _senderLoops[autoResponder] = cts;

            var g711 = new G711Codec();
            _ = Task.Run(async () => {
                var token = cts.Token;
                var frameIntervalMs = profile.PtimeMs > 0 ? profile.PtimeMs : 20;

                while (!token.IsCancellationRequested) {
                    try {
                        var frame = audioBridge.GetNextOutgoingFrame();
                        if (frame != null && frame.Length > 0 && rtpSession != null && !rtpSession.IsClosed) {
                            byte[] payload;
                            if (profile.Codec == AudioCodec.PCMU) {
                                payload = g711.EncodeMuLaw(frame);
                            } else {
                                payload = g711.EncodeALaw(frame);
                            }

                            if (payload.Length > 0) {
                                rtpSession.SendAudio((uint)payload.Length, payload);
                            }
                        }
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error in AI RTP sender loop");
                        await Task.Delay(100, token);
                    }

                    try {
                        await Task.Delay(frameIntervalMs, token);
                    } catch { /* cancellation */ }
                }

                _logger.LogDebug("AI RTP sender loop stopped");
            });

            return autoResponder;
        }

        private void ConnectAudioBridge(
            AIAutoResponder autoResponder,
            IAudioBridge audioBridge,
            QueueAudioPlaybackSource playbackSource,
            IVoiceActivityDetector vad) {

            audioBridge.IncomingAudioReceived += (audioFrame) => {
                try {
                    autoResponder.OnUplinkPcmFrame(audioFrame);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error processing incoming audio in AutoResponder");
                }
            };

            audioBridge.OutgoingAudioRequested += (requestedFrame) => {
                try {
                    var playbackFrame = playbackSource.ReadNextPcmFrame();
                    if (playbackFrame != null && playbackFrame.Length > 0) {
                        Array.Copy(playbackFrame, requestedFrame, Math.Min(playbackFrame.Length, requestedFrame.Length));
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error providing outgoing audio from AutoResponder");
                }
            };
        }
    }
}