using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace AI.Caller.Core.Media.Adapters {
    public class AsrEngineAdapter : IAsrEngine, IDisposable {
        private bool _disposed;

        private readonly object _lock = new();
        private readonly ILogger _logger;
        private readonly int _modelSampleRate = 16000;
        private readonly OfflineRecognizer _recognizer;
        private readonly AudioResamplerCrossType<byte, float> _resampler;
        public AsrEngineAdapter(IOptions<RecognizerSettings> recognizeOption, ILogger<AsrEngineAdapter> logger) {
            var config = new OfflineRecognizerConfig();
            config.ModelConfig.Debug = recognizeOption.Value.Debug;
            config.ModelConfig.Tokens = Path.Combine(recognizeOption.Value.ModelFolder, recognizeOption.Value.Tokens);
            config.ModelConfig.Paraformer.Model = Path.Combine(recognizeOption.Value.ModelFolder, recognizeOption.Value.ModelFile);

            _logger     = logger;
            _recognizer = new OfflineRecognizer(config);
            _resampler  = new AudioResamplerCrossType<byte, float>(8000, _modelSampleRate, logger);
        }

        public string RecognizeStream(byte[] pcmBytes) {
            try {
                var resampledSegment = _resampler.Resample(pcmBytes);
                var resampled = resampledSegment.ToArray();
                lock (_lock) {
                    var stream = _recognizer.CreateStream();
                    stream.AcceptWaveform(_modelSampleRate, resampled);
                    _recognizer.Decode(stream);
                    var text = stream.Result?.Text ?? string.Empty;
                    _logger.LogDebug("ASR result: {Text}", text);
                    return text ?? string.Empty;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error during ASR RecognizeStream");
                return string.Empty;
            }
        }

        protected virtual void Dispose(bool disposing) {
            if (_disposed) return;
            if (disposing) {
                try {
                    _recognizer?.Dispose();
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Exception while disposing OfflineRecognizer");
                }
                try {
                    _resampler?.Dispose();
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Exception while disposing resampler");
                }
            }
            _disposed = true;
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}