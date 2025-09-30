using AI.Caller.Core.Interfaces;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Media.Sources;
using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AI.Caller.Core {
    public class AIAutoResponderFactory : IAIAutoResponderFactory {
        private readonly ILogger _logger;
        private readonly ITTSEngine _ttsEngine;
        private readonly G711Codec _g711 = new();
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

        public AIAutoResponder CreateAutoResponder(MediaProfile profile) {
            var playbackSource = new QueueAudioPlaybackSource(_logger);
            playbackSource.Init(profile);

            var vad = new FfmpegEnhancedVad(
                inputSampleRate: profile.SampleRate,    // 8000Hz
                targetSampleRate: profile.SampleRate,   // 8000Hz  
                hpCutoffHz: 80                          // 电话频带下限约300Hz，设80Hz高通
            );

            vad.Configure(
                energyThreshold: 0.005f,    // 作为初始噪声地板，会自适应调整
                enterSpeakingMs: 150,       // 稍快进入Speaking
                resumeSilenceMs: 400,       // 减少恢复静音时间
                sampleRate: profile.SampleRate,
                frameMs: profile.PtimeMs
            );

            vad.SetAdaptive(
                emaAlpha: 0.08f,           // 稍快的噪声地板适应
                enterMarginDb: 8f,         // 进入Speaking需要8dB以上
                resumeMarginDb: 4f         // 恢复静音需要4dB以上
            );

            var autoResponder = new AIAutoResponder(
                _logger, 
                _ttsEngine, 
                playbackSource, 
                vad, 
                profile, 
                _g711);

            return autoResponder;
        }
    }
}