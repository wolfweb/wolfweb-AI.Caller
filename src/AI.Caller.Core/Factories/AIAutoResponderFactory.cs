using AI.Caller.Core.Interfaces;
using AI.Caller.Core.Media;
using AI.Caller.Core.Services;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core {
    public class AIAutoResponderFactory : IAIAutoResponderFactory {
        private readonly ILogger _logger;
        private readonly ITTSEngine _ttsEngine;
        private readonly AudioCodecFactory _codecFactory;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IDtmfService? _dtmfService;

        public AIAutoResponderFactory(
            ILogger<AIAutoResponder> logger,
            ITTSEngine ttsEngine,
            ILoggerFactory loggerFactory,
            AudioCodecFactory codecFactory,
            IDtmfService? dtmfService = null) {
            _logger = logger;
            _ttsEngine = ttsEngine;
            _dtmfService = dtmfService;
            _codecFactory = codecFactory;
            _loggerFactory = loggerFactory;
        }

        public AIAutoResponder CreateAutoResponder(MediaProfile profile) {
            var autoResponder = new AIAutoResponder(
                _loggerFactory, 
                _ttsEngine, 
                profile, 
                _codecFactory,
                _dtmfService);

            return autoResponder;
        }
    }
}