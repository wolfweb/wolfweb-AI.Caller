using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Adapters;
using AI.Caller.Core.Media.Encoders;
using FFmpeg.AutoGen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace AI.Caller.Core.Tests.Media {
    public class AudioProcessingPipelineTests {
        private readonly ITestOutputHelper _output;

        public AudioProcessingPipelineTests(ITestOutputHelper output) {
            _output = output;
            ffmpeg.RootPath = "F:/Sources/ffmpeg-fw/FFmpeg.AutoGen/FFmpeg/bin/x64";
        }

        [Theory]
        [InlineData(@"E:/Document/AI-models/tts/sherpa-onnx-vits-zh-ll")]
        public async Task StreamingPipeline_ShouldSimulateRealtimeProcessing(string modelDir) {
            if (string.IsNullOrWhiteSpace(modelDir) || !Directory.Exists(modelDir)) {
                _output.WriteLine($"Skipping test: TTS model directory not found at '{modelDir}'.");
                return;
            }

            var g711Codec = new G711Codec();
            var mediaProfile = new MediaProfile(AudioCodec.PCMU, 0, 8000, 20, 1);
            var logger = new Mock<ILogger>().Object;
            using var resampler = new AudioResampler<float>(16000, mediaProfile.SampleRate, logger);
            string textToSynthesize = "您好，欢迎致电我们公司，我是AI客服小助手。";

            var services = new ServiceCollection()
                .AddLogging()
                .Configure<TTSSettings>(x => {
                    x.Enabled = true; x.ModelFolder = modelDir; x.ModelFile = "model.onnx";
                    x.LexiconFile = "lexicon.txt"; x.TokensFile = "tokens.txt"; x.DictDir = "dict";
                    x.NumThreads = 1; x.Debug = 1; x.Provider = "cpu";
                    x.RuleFsts = "date.fst,new_heteronym.fst,number.fst,phone.fst";
                })
                .AddSingleton<ITTSEngine, TTSEngineAdapter>()
                .BuildServiceProvider();
            var realTtsEngine = services.GetRequiredService<ITTSEngine>();

            var stage1_TtsFloat16k_Chunks = new List<float[]>();
            var stage2_ResampledPcm8k_Stream = new List<byte>();
            var stage3_FinalPcm8k_Frames = new List<byte>();

            _output.WriteLine("Starting streaming simulation...");
            byte[] audioBuffer = Array.Empty<byte>(); // Simulates AIAutoResponder's internal buffer
            int frameSizeInBytes = mediaProfile.SamplesPerFrame * 2; // 320 bytes

            await foreach (var ttsChunk in realTtsEngine.SynthesizeStreamAsync(textToSynthesize, 0, 1.0f)) {
                if (ttsChunk.FloatData == null || ttsChunk.FloatData.Length == 0) continue;

                stage1_TtsFloat16k_Chunks.Add(ttsChunk.FloatData);
                _output.WriteLine($"Received TTS chunk with {ttsChunk.FloatData.Length} samples.");

                var resampledFloat8k = resampler.Resample(ttsChunk.FloatData);
                var pcmChunk8k = ConvertFloatTo16BitPcm(resampledFloat8k);
                stage2_ResampledPcm8k_Stream.AddRange(pcmChunk8k);
                _output.WriteLine($"Resampled to {resampledFloat8k.Length} samples ({pcmChunk8k.Length} bytes).");

                var combinedBuffer = new byte[audioBuffer.Length + pcmChunk8k.Length];
                Buffer.BlockCopy(audioBuffer, 0, combinedBuffer, 0, audioBuffer.Length);
                Buffer.BlockCopy(pcmChunk8k, 0, combinedBuffer, audioBuffer.Length, pcmChunk8k.Length);

                int offset = 0;
                while (offset + frameSizeInBytes <= combinedBuffer.Length) {
                    var pcmFrame = new Span<byte>(combinedBuffer, offset, frameSizeInBytes);
                    var g711Frame = g711Codec.EncodeMuLaw(pcmFrame);
                    var decodedPcmFrame = g711Codec.DecodeG711MuLaw(g711Frame);
                    stage3_FinalPcm8k_Frames.AddRange(decodedPcmFrame);
                    offset += frameSizeInBytes;
                }

                int remainingBytes = combinedBuffer.Length - offset;
                audioBuffer = new byte[remainingBytes];
                Buffer.BlockCopy(combinedBuffer, offset, audioBuffer, 0, remainingBytes);
                _output.WriteLine($"Framed {offset / frameSizeInBytes} frames. Remaining buffer: {remainingBytes} bytes.");
            }

            if (audioBuffer.Length > 0) {
                var finalFrame = new byte[frameSizeInBytes];
                Buffer.BlockCopy(audioBuffer, 0, finalFrame, 0, audioBuffer.Length);
                var g711Frame = g711Codec.EncodeMuLaw(finalFrame);
                var decodedPcmFrame = g711Codec.DecodeG711MuLaw(g711Frame);
                stage3_FinalPcm8k_Frames.AddRange(decodedPcmFrame);
                _output.WriteLine($"Flushed final {audioBuffer.Length} bytes into one frame.");
            }

            var fullTtsStream = stage1_TtsFloat16k_Chunks.SelectMany(x => x).ToArray();
            var stage1FileName = "streaming_stage1_tts_16k.wav";
            SaveFloatToWav(stage1FileName, fullTtsStream, 16000);
            _output.WriteLine($"Saved Stage 1 to: {Path.GetFullPath(stage1FileName)}");

            var stage2FileName = "streaming_stage2_resampled_8k.wav";
            SaveToWav(stage2FileName, stage2_ResampledPcm8k_Stream.ToArray(), mediaProfile.SampleRate);
            _output.WriteLine($"Saved Stage 2 to: {Path.GetFullPath(stage2FileName)}");

            var stage3FileName = "streaming_stage3_final_8k.wav";
            SaveToWav(stage3FileName, stage3_FinalPcm8k_Frames.ToArray(), mediaProfile.SampleRate);
            _output.WriteLine($"Saved Stage 3 to: {Path.GetFullPath(stage3FileName)}");

            Assert.True(File.Exists(stage1FileName) && new FileInfo(stage1FileName).Length > 44);
            Assert.True(File.Exists(stage2FileName) && new FileInfo(stage2FileName).Length > 44);
            Assert.True(File.Exists(stage3FileName) && new FileInfo(stage3FileName).Length > 44);
        }

        private byte[] ConvertFloatTo16BitPcm(float[] floatData) {
            var pcmData = new byte[floatData.Length * 2];
            for (int i = 0; i < floatData.Length; i++) {
                float sample = Math.Clamp(floatData[i], -1f, 1f);
                short shortSample = (short)(sample * 32767f);
                pcmData[i * 2] = (byte)(shortSample & 0xFF);
                pcmData[i * 2 + 1] = (byte)(shortSample >> 8);
            }
            return pcmData;
        }

        private void SaveFloatToWav(string filename, float[] floatData, int sampleRate) {
            var pcmData = ConvertFloatTo16BitPcm(floatData);
            SaveToWav(filename, pcmData, sampleRate);
        }

        private void SaveToWav(string filename, byte[] pcmData, int sampleRate) {
            var header = CreateWavHeader(pcmData.Length, sampleRate);
            using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write)) {
                fs.Write(header, 0, header.Length);
                fs.Write(pcmData, 0, pcmData.Length);
            }
        }

        private byte[] CreateWavHeader(int dataLength, int sampleRate) {
            var header = new byte[44];
            var fileSize = dataLength + 36;

            System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
            BitConverter.GetBytes(fileSize).CopyTo(header, 4);
            System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);
            System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
            BitConverter.GetBytes(16).CopyTo(header, 16);
            BitConverter.GetBytes((short)1).CopyTo(header, 20);
            BitConverter.GetBytes((short)1).CopyTo(header, 22);
            BitConverter.GetBytes(sampleRate).CopyTo(header, 24);
            BitConverter.GetBytes(sampleRate * 2).CopyTo(header, 28);
            BitConverter.GetBytes((short)2).CopyTo(header, 32);
            BitConverter.GetBytes((short)16).CopyTo(header, 34);
            System.Text.Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
            BitConverter.GetBytes(dataLength).CopyTo(header, 40);

            return header;
        }
    }
}