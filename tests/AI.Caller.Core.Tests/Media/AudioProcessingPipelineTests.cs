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
        public async Task FullPipeline_UsingRealClasses_Should_Produce_ClearWavFile(string modelDir) {
            if (string.IsNullOrWhiteSpace(modelDir) || !Directory.Exists(modelDir)) {
                _output.WriteLine($"Skipping test: TTS model directory not found at '{modelDir}'.");
                return;
            }

            // 1. Setup Dependencies
            var autoResponderLogger = new Mock<ILogger<AIAutoResponder>>().Object;
            var bridgeLogger = new Mock<ILogger<AudioBridge>>().Object;
            var serviceProviderMock = new Mock<IServiceProvider>().Object; // Still needed for factory constructor
            var g711Codec = new G711Codec();
            var mediaProfile = new MediaProfile(AudioCodec.PCMU, 0, 8000, 20, 1);

            // 2. Setup Real TTSEngine
            var services = new ServiceCollection()
                .AddLogging()
                .Configure<TTSSettings>(x => {
                    x.Enabled = true;
                    x.ModelFolder = modelDir;
                    x.ModelFile = "model.onnx";
                    x.LexiconFile = "lexicon.txt";
                    x.TokensFile = "tokens.txt";
                    x.DictDir = "dict";
                    x.NumThreads = 1;
                    x.Debug = 1;
                    x.Provider = "cpu";
                    x.RuleFsts = "date.fst,new_heteronym.fst,number.fst,phone.fst";
                })
                .AddSingleton<ITTSEngine, TTSEngineAdapter>()
                .BuildServiceProvider();
            var realTtsEngine = services.GetRequiredService<ITTSEngine>();
            _output.WriteLine("Real TTSEngineAdapter instance created.");

            // 3. Instantiate REAL AudioBridge
            var audioBridge = new AudioBridge(bridgeLogger);
            audioBridge.Initialize(mediaProfile);
            audioBridge.Start();
            _output.WriteLine("Real AudioBridge instance created and started.");

            // 4. Instantiate Real Factory and Create the System Under Test (SUT)
            var factory = new AIAutoResponderFactory(autoResponderLogger, realTtsEngine, serviceProviderMock);
            var autoResponder = factory.CreateAutoResponder(audioBridge, mediaProfile);
            _output.WriteLine("Real AIAutoResponderFactory and AIAutoResponder created.");

            // 5. Start the process
            await autoResponder.StartAsync(CancellationToken.None);
            string textToSynthesize = "您好，欢迎致电我们公司，我是AI客服小助手。";
            _ = autoResponder.PlayScriptAsync(textToSynthesize);
            _output.WriteLine($"Audio generation started for text: '{textToSynthesize}'");

            // 6. Pull data from the real AudioBridge and process it
            var finalPcmData = new List<byte>();
            int silentFramesCount = 0;
            const int maxSilentFrames = 50; // Stop after 1 second of silence
            const double silenceThreshold = 0.001; // RMS energy threshold for silence

            while (silentFramesCount < maxSilentFrames) {
                // This is the crucial change: we call the real method
                var g711Frame = audioBridge.GetNextOutgoingFrame();

                // To reliably detect silence, we must decode and check energy
                var decodedPcmFrame = g711Codec.DecodeG711MuLaw(g711Frame);
                double rmsEnergy = CalculateRmsEnergy(decodedPcmFrame);

                if (rmsEnergy < silenceThreshold) {
                    silentFramesCount++;
                } else {
                    silentFramesCount = 0; // Reset on non-silent frame
                    finalPcmData.AddRange(decodedPcmFrame);
                }

                await Task.Delay(mediaProfile.PtimeMs); // Simulate the 20ms tick
            }

            _output.WriteLine($"Collected {finalPcmData.Count} bytes of final PCM data after detecting end of speech.");
            await autoResponder.StopAsync();
            audioBridge.Dispose();

            // 7. Save to WAV file
            string outputFileName = "test_full_real_pipeline_output.wav";
            SaveToWav(outputFileName, finalPcmData.ToArray(), mediaProfile.SampleRate);
            _output.WriteLine($"Output saved to file: {Path.GetFullPath(outputFileName)}");

            // 8. Assert
            Assert.True(File.Exists(outputFileName));
            var fileInfo = new FileInfo(outputFileName);
            Assert.True(fileInfo.Length > 44, "File should be larger than WAV header.");
            Assert.True(finalPcmData.Count > 0, "Should have collected some audio data.");
        }

        private double CalculateRmsEnergy(byte[] pcmData) {
            if (pcmData == null || pcmData.Length == 0) return 0;

            double sumOfSquares = 0;
            for (int i = 0; i < pcmData.Length; i += 2) {
                short sample = (short)(pcmData[i] | (pcmData[i + 1] << 8));
                double floatSample = sample / 32768.0;
                sumOfSquares += floatSample * floatSample;
            }
            return Math.Sqrt(sumOfSquares / (pcmData.Length / 2.0));
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