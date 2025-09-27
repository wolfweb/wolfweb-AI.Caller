using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Adapters;
using Microsoft.Extensions.DependencyInjection;
using PortAudioSharp;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Xunit;

namespace AI.Caller.Core.Tests.Media.Tts {
    public class SherpaIntegrationTests {
        [Theory]
        [InlineData(@"E:/Document/AI-models/tts/sherpa-onnx-vits-zh-ll")]
        public async Task SynthesizeToFile_Sherpa_LocalOnnx_WavOk(string modelDir) {
            if (string.IsNullOrWhiteSpace(modelDir) || !Directory.Exists(modelDir))
                return;

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

            PortAudio.Initialize();

            int deviceIndex = PortAudio.DefaultOutputDevice;
            var info = PortAudio.GetDeviceInfo(deviceIndex);

            var param = new StreamParameters();
            param.device = deviceIndex;
            param.channelCount = 1;
            param.sampleFormat = SampleFormat.Float32;
            param.suggestedLatency = info.defaultLowOutputLatency;
            param.hostApiSpecificStreamInfo = IntPtr.Zero;
            var dataItems = new BlockingCollection<float[]>();

            var playFinished = false;

            float[]? lastSampleArray = null;
            int lastIndex = 0; // not played

            PortAudioSharp.Stream.Callback playCallback = (IntPtr input, IntPtr output,
                UInt32 frameCount,
                ref StreamCallbackTimeInfo timeInfo,
                StreamCallbackFlags statusFlags,
                IntPtr userData
                ) =>
            {
                if (dataItems.IsCompleted && lastSampleArray == null && lastIndex == 0) {
                    Console.WriteLine($"Finished playing");
                    playFinished = true;
                    return StreamCallbackResult.Complete;
                }

                int expected = Convert.ToInt32(frameCount);
                int i = 0;

                while ((lastSampleArray != null || dataItems.Count != 0) && (i < expected)) {
                    int needed = expected - i;

                    if (lastSampleArray != null) {
                        int remaining = lastSampleArray.Length - lastIndex;
                        if (remaining >= needed) {
                            float[] this_block = lastSampleArray.Skip(lastIndex).Take(needed).ToArray();
                            lastIndex += needed;
                            if (lastIndex == lastSampleArray.Length) {
                                lastSampleArray = null;
                                lastIndex = 0;
                            }

                            Marshal.Copy(this_block, 0, IntPtr.Add(output, i * sizeof(float)), needed);
                            return StreamCallbackResult.Continue;
                        }

                        float[] this_block2 = lastSampleArray.Skip(lastIndex).Take(remaining).ToArray();
                        lastIndex = 0;
                        lastSampleArray = null;

                        Marshal.Copy(this_block2, 0, IntPtr.Add(output, i * sizeof(float)), remaining);
                        i += remaining;
                        continue;
                    }

                    if (dataItems.Count != 0) {
                        lastSampleArray = dataItems.Take();
                        lastIndex = 0;
                    }
                }

                if (i < expected) {
                    int sizeInBytes = (expected - i) * 4;
                    Marshal.Copy(new byte[sizeInBytes], 0, IntPtr.Add(output, i * sizeof(float)), sizeInBytes);
                }

                return StreamCallbackResult.Continue;
            };

            PortAudioSharp.Stream stream = new PortAudioSharp.Stream(inParams: null, outParams: param, sampleRate: 16000,
                framesPerBuffer: 0,
                streamFlags: StreamFlags.ClipOff,
                callback: playCallback,
                userData: IntPtr.Zero
                );

            stream.Start();

            string text = "您好，欢迎致电。";

            var engine = services.GetService<ITTSEngine>();
            await foreach (var data in engine.SynthesizeStreamAsync(text, 1)) {
                dataItems.Add(data.FloatData);
            }

            dataItems.CompleteAdding();
            await Task.Delay(1000);
        }
    }
}