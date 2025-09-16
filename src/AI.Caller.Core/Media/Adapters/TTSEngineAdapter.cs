using AI.Caller.Core.Media.Tts;
using Org.BouncyCastle.Asn1.X509;
using SherpaOnnx;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace AI.Caller.Core.Media.Adapters {
    public sealed class TTSEngineAdapter : ITtsEngine {
        private readonly OfflineTts _tts;
        public TTSEngineAdapter() {
            var folder = "";
            var config = new OfflineTtsConfig();

            config.Model.Vits.Model = Path.Combine(folder, "model.onnx");
            config.Model.Vits.Lexicon = Path.Combine(folder, "lexicon.txt");
            config.Model.Vits.Tokens = Path.Combine(folder, "tokens.txt");
            config.Model.Vits.DictDir = Path.Combine(folder, "dict");

            config.Model.NumThreads = 1;
            config.Model.Debug = 1;
            config.Model.Provider = "cpu";
            config.RuleFsts = $"{Path.Combine(folder, "date.fst")},{Path.Combine(folder, "new_heteronym.fst")},{Path.Combine(folder, "number.fst")},{Path.Combine(folder, "phone.fst")}";

            _tts = new OfflineTts(config);
        }

        public async IAsyncEnumerable<float[]> SynthesizeStreamAsync(string text, int speakerId, float speed = 1.0f) {
            var channel = Channel.CreateUnbounded<float[]>();

            _ = Task.Run(() => { 
                _tts.GenerateWithCallback(text, speed, speakerId, new OfflineTtsCallback((IntPtr samples, int n) => {
                    float[] data = new float[n];
                    Marshal.Copy(samples, data, 0, n);
                    channel.Writer.TryWrite(data);
                    return 1;
                }));
                channel.Writer.Complete();
            });

            await foreach (var item in channel.Reader.ReadAllAsync()) {
                yield return item;
            }
        }
    }
}