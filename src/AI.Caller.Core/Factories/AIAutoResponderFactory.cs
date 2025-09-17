using AI.Caller.Core.Interfaces;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Interfaces;
using AI.Caller.Core.Media.Sources;
using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core {
    public class AIAutoResponderFactory : IAIAutoResponderFactory {
        private readonly ILogger<AIAutoResponder> _logger;
        private readonly ITTSEngine _ttsEngine;
        private readonly IServiceProvider _serviceProvider;

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