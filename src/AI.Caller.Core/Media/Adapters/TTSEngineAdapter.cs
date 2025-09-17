using AI.Caller.Core.Configuration;
using AI.Caller.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace AI.Caller.Core.Media.Adapters {
    public sealed class TTSEngineAdapter : ITTSEngine {
        private readonly OfflineTts? _tts;
        private readonly ILogger<TTSEngineAdapter> _logger;
        private readonly TTSSettings _settings;
        private readonly bool _isEnabled;

        public TTSEngineAdapter(IOptions<TTSSettings> settings, ILogger<TTSEngineAdapter> logger) {
            _logger = logger;
            _settings = settings.Value;
            _isEnabled = _settings.Enabled;

            if (!_isEnabled) {
                _logger.LogWarning("TTS is disabled in configuration");
                return;
            }

            try {
                var config = new OfflineTtsConfig();

                // 验证模型文件夹是否存在
                if (string.IsNullOrEmpty(_settings.ModelFolder) || !Directory.Exists(_settings.ModelFolder)) {
                    _logger.LogError($"TTS model folder not found: {_settings.ModelFolder}");
                    _isEnabled = false;
                    return;
                }

                // 配置模型路径
                config.Model.Vits.Model = Path.Combine(_settings.ModelFolder, _settings.ModelFile);
                config.Model.Vits.Lexicon = Path.Combine(_settings.ModelFolder, _settings.LexiconFile);
                config.Model.Vits.Tokens = Path.Combine(_settings.ModelFolder, _settings.TokensFile);
                config.Model.Vits.DictDir = Path.Combine(_settings.ModelFolder, _settings.DictDir);

                // 验证必需文件是否存在
                if (!File.Exists(config.Model.Vits.Model)) {
                    _logger.LogError($"TTS model file not found: {config.Model.Vits.Model}");
                    _isEnabled = false;
                    return;
                }

                config.Model.NumThreads = _settings.NumThreads;
                config.Model.Debug = _settings.Debug;
                config.Model.Provider = _settings.Provider;

                // 配置规则FST文件
                if (!string.IsNullOrEmpty(_settings.RuleFsts)) {
                    var fstFiles = _settings.RuleFsts.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(f => Path.Combine(_settings.ModelFolder, f.Trim()))
                        .Where(File.Exists)
                        .ToArray();
                    
                    config.RuleFsts = string.Join(",", fstFiles);
                    _logger.LogDebug($"Configured {fstFiles.Length} FST rule files");
                }

                _tts = new OfflineTts(config);
                _logger.LogInformation("TTS engine initialized successfully");
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to initialize TTS engine");
                _isEnabled = false;
            }
        }

        public async IAsyncEnumerable<AudioData> SynthesizeStreamAsync(string text, int speakerId, float speed = 1.0f) {
            if (!_isEnabled || _tts == null) {
                _logger.LogWarning("TTS engine is not available, returning empty audio stream");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(text)) {
                _logger.LogWarning("Empty text provided for TTS synthesis");
                yield break;
            }

            var channel = Channel.CreateUnbounded<float[]>();

            _ = Task.Run(() => {
                try {
                    _logger.LogDebug($"Starting TTS synthesis for text: '{text}' (length: {text.Length})");
                    
                    _tts.GenerateWithCallback(text, speed, speakerId, new OfflineTtsCallback((IntPtr samples, int n) => {
                        try {
                            if (n > 0) {
                                float[] data = new float[n];
                                Marshal.Copy(samples, data, 0, n);
                                channel.Writer.TryWrite(data);
                            }
                            return 1;
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Error in TTS callback");
                            return 0;
                        }
                    }));
                    
                    _logger.LogDebug("TTS synthesis completed");
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error during TTS synthesis");
                } finally {
                    channel.Writer.Complete();
                }
            });

            await foreach (var item in channel.Reader.ReadAllAsync()) {
                yield return new AudioData {
                    FloatData = item,
                    Format = AudioDataFormat.PCM_Float
                };
            }
        }
    }
}