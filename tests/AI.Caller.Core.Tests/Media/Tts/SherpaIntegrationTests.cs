using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI.Caller.Core.Tests.Media.Tts;
using Xunit;
using SherpaOnnx;

namespace AI.Caller.Core.Tests.Media.Tts {
    public class SherpaIntegrationTests {
        [Fact]
        public void SynthesizeToFile_Sherpa_LocalOnnx_WavOk() {
            var modelDir = Environment.GetEnvironmentVariable("SHERPA_TTS_MODEL_DIR");
            if (string.IsNullOrWhiteSpace(modelDir) || !Directory.Exists(modelDir))
                return;

            string voice = Environment.GetEnvironmentVariable("SHERPA_VOICE_ID") ?? "cn_male_01";
            int sampleRate = int.TryParse(Environment.GetEnvironmentVariable("SHERPA_SR"), out var sr) ? sr : 16000;

            string outDir = Path.Combine(Path.GetTempPath(), "sherpa_tests");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, $"tts_{DateTime.UtcNow.Ticks}.wav");

            string text = "您好，欢迎致电。";

            var config = new OfflineTtsConfig();

            var tts = new OfflineTts(config);
            var audio = tts.Generate(text, 1.0f, 1);
            audio.SaveToWaveFile(outPath);

            Assert.True(File.Exists(outPath));
            var (sr2, ch, bits, dataLen) = TtsTestUtils.ReadWavHeader(outPath);
            Assert.Equal(sampleRate, sr2);
            Assert.Equal(1, ch);
            Assert.Equal(16, bits);
            Assert.True(dataLen > 0);
        }
    }
}