using AI.Caller.Core.Media.Tts;
using AI.Caller.Core.Tests.Media.Tts;
using KokoroSharp;
using KokoroSharp.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AI.Caller.Core.Tests.Media.Tts {
    public class KokoroSharpIntegrationTests {
        [Fact]
        public void SynthesizeToFile_KokoroSharp_CNVoice_WavOk() {
            int sampleRate = int.TryParse(Environment.GetEnvironmentVariable("KOKORO_SR"), out var sr) ? sr : 16000;

            string outDir = Path.Combine(Path.GetTempPath(), "kokoro_tests");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, $"tts_{DateTime.UtcNow.Ticks}.wav");

            string text = "您好，我是人工智能客服。";

            KokoroTTS tts = KokoroTTS.LoadModel();
            KokoroVoice heartVoice = KokoroVoiceManager.GetVoice("af_heart");
            tts.SpeakFast(text, heartVoice);

            Assert.True(File.Exists(outPath));
            var (sr2, ch, bits, dataLen) = TtsTestUtils.ReadWavHeader(outPath);
            Assert.Equal(sampleRate, sr2);
            Assert.Equal(1, ch);
            Assert.Equal(16, bits);
            Assert.True(dataLen > 0);
        }
    }
}