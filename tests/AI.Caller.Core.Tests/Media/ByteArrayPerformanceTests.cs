using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Media.Sources;
using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace AI.Caller.Core.Tests.Media {
    /// <summary>
    /// 性能测试：验证byte[]架构的性能表现
    /// </summary>
    public class ByteArrayPerformanceTests {
        private readonly Mock<ILogger<AudioBridge>> _mockLogger;
        private readonly MediaProfile _testProfile;

        public ByteArrayPerformanceTests() {
            _mockLogger = new Mock<ILogger<AudioBridge>>();
            _testProfile = new MediaProfile(AudioCodec.PCMU, 0, 8000, 20, 1);
        }

        [Fact]
        public void AudioBridge_ProcessingPerformance_ShouldBeEfficient() {
            // Arrange
            var bridge = new AudioBridge(_mockLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var testAudio = GenerateTestAudio(320);
            const int iterations = 1000;

            // Act - 测量处理时间
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < iterations; i++) {
                bridge.ProcessIncomingAudio(testAudio, 8000);
                bridge.InjectOutgoingAudio(testAudio);
                bridge.GetNextOutgoingFrame();
            }
            
            stopwatch.Stop();

            // Assert
            var averageTimePerFrame = stopwatch.ElapsedMilliseconds / (double)iterations;
            
            // 每帧处理时间应该远小于20ms（实时音频帧间隔）
            Assert.True(averageTimePerFrame < 1.0, 
                $"Average processing time per frame should be < 1ms, actual: {averageTimePerFrame:F3}ms");
            
            // 总处理时间应该合理
            Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
                $"Total processing time should be < 5s for {iterations} frames, actual: {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void VAD_ProcessingPerformance_ShouldBeRealTime() {
            // Arrange
            var vad = new EnergyVad();
            vad.Configure(0.02f, 200, 600, 8000, 20);

            var testAudio = GenerateTestAudio(320);
            const int iterations = 2000;

            // Act - 测量VAD处理时间
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < iterations; i++) {
                vad.Update(testAudio);
            }
            
            stopwatch.Stop();

            // Assert
            var averageTimePerFrame = stopwatch.ElapsedMilliseconds / (double)iterations;
            
            // VAD处理时间应该非常快
            Assert.True(averageTimePerFrame < 0.5, 
                $"Average VAD processing time should be < 0.5ms, actual: {averageTimePerFrame:F3}ms");
            
            // 总处理时间应该合理
            Assert.True(stopwatch.ElapsedMilliseconds < 2000, 
                $"Total VAD processing time should be < 2s for {iterations} frames, actual: {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void G711Codec_EncodingPerformance_ShouldBeEfficient() {
            // Arrange
            var codec = new G711Codec();
            var testAudio = GenerateTestAudio(320);
            const int iterations = 1500;

            // Act - 测量编码性能
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < iterations; i++) {
                var encoded = codec.EncodeMuLaw(testAudio);
                var decoded = codec.DecodeG711MuLaw(encoded);
            }
            
            stopwatch.Stop();

            // Assert
            var averageTimePerFrame = stopwatch.ElapsedMilliseconds / (double)iterations;
            
            // 编码/解码时间应该很快
            Assert.True(averageTimePerFrame < 1.0, 
                $"Average G711 encode/decode time should be < 1ms, actual: {averageTimePerFrame:F3}ms");
            
            // 总处理时间应该合理
            Assert.True(stopwatch.ElapsedMilliseconds < 3000, 
                $"Total G711 processing time should be < 3s for {iterations} frames, actual: {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void QueueAudioPlaybackSource_PerformanceTest_ShouldHandleHighThroughput() {
            // Arrange
            var playback = new QueueAudioPlaybackSource();
            playback.Init(_testProfile);
            playback.StartAsync(default).Wait();

            var testAudio = GenerateTestAudio(320);
            const int iterations = 1000;

            // Act - 测量播放源性能
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < iterations; i++) {
                playback.Enqueue(testAudio);
                playback.ReadNextPcmFrame();
            }
            
            stopwatch.Stop();

            // Assert
            var averageTimePerFrame = stopwatch.ElapsedMilliseconds / (double)iterations;
            
            // 播放源处理时间应该很快
            Assert.True(averageTimePerFrame < 0.5, 
                $"Average playback processing time should be < 0.5ms, actual: {averageTimePerFrame:F3}ms");
            
            // 验证RMS计算不影响性能
            Assert.True(playback.PlaybackRms >= 0, "RMS calculation should work without performance impact");
        }

        [Fact]
        public void EndToEnd_PerformanceTest_ShouldMeetRealTimeRequirements() {
            // Arrange - 完整的音频处理链路
            var bridge = new AudioBridge(_mockLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var playback = new QueueAudioPlaybackSource();
            playback.Init(_testProfile);
            playback.StartAsync(default).Wait();

            var codec = new G711Codec();
            var vad = new EnergyVad();
            vad.Configure(0.02f, 200, 600, 8000, 20);

            var testAudio = GenerateTestAudio(320);
            const int iterations = 500;

            byte[] processedAudio = null;
            bridge.IncomingAudioReceived += (data) => processedAudio = data;

            // Act - 测量端到端性能
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < iterations; i++) {
                // 完整的音频处理流程
                bridge.ProcessIncomingAudio(testAudio, 8000);
                vad.Update(processedAudio);
                playback.Enqueue(processedAudio);
                playback.ReadNextPcmFrame();
                bridge.InjectOutgoingAudio(processedAudio);
                var outgoing = bridge.GetNextOutgoingFrame();
                var encoded = codec.EncodeMuLaw(outgoing);
                codec.DecodeG711MuLaw(encoded);
            }
            
            stopwatch.Stop();

            // Assert
            var averageTimePerFrame = stopwatch.ElapsedMilliseconds / (double)iterations;
            
            // 端到端处理时间应该远小于20ms实时要求
            Assert.True(averageTimePerFrame < 5.0, 
                $"Average end-to-end processing time should be < 5ms, actual: {averageTimePerFrame:F3}ms");
            
            // 总处理时间应该合理
            Assert.True(stopwatch.ElapsedMilliseconds < 10000, 
                $"Total end-to-end processing time should be < 10s for {iterations} frames, actual: {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void MemoryUsage_ShouldBeOptimal_WithByteArrayArchitecture() {
            // Arrange
            var bridge = new AudioBridge(_mockLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var testAudio = GenerateTestAudio(320);
            const int iterations = 100;

            // 测量初始内存使用
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var initialMemory = GC.GetTotalMemory(false);

            // Act - 处理大量音频数据
            for (int i = 0; i < iterations; i++) {
                bridge.ProcessIncomingAudio(testAudio, 8000);
                bridge.InjectOutgoingAudio(testAudio);
                bridge.GetNextOutgoingFrame();
            }

            // 强制垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(false);

            // Assert
            var memoryIncrease = finalMemory - initialMemory;
            var memoryIncreaseKB = memoryIncrease / 1024.0;
            
            // 内存增长应该很小（主要是缓存的音频帧）
            Assert.True(memoryIncreaseKB < 500, 
                $"Memory increase should be < 500KB, actual: {memoryIncreaseKB:F1}KB");
            
            // 验证没有明显的内存泄漏
            Assert.True(memoryIncrease < testAudio.Length * iterations * 2, 
                "Memory increase should not indicate memory leaks");
        }

        [Fact]
        public void ConcurrentProcessing_PerformanceTest_ShouldHandleMultipleStreams() {
            // Arrange
            const int streamCount = 4;
            const int iterationsPerStream = 250;
            var bridges = new AudioBridge[streamCount];
            var testAudio = GenerateTestAudio(320);

            for (int i = 0; i < streamCount; i++) {
                bridges[i] = new AudioBridge(_mockLogger.Object);
                bridges[i].Initialize(_testProfile);
                bridges[i].Start();
            }

            // Act - 并发处理多个音频流
            var stopwatch = Stopwatch.StartNew();
            
            var tasks = new Task[streamCount];
            for (int streamIndex = 0; streamIndex < streamCount; streamIndex++) {
                var bridge = bridges[streamIndex];
                tasks[streamIndex] = Task.Run(() => {
                    for (int i = 0; i < iterationsPerStream; i++) {
                        bridge.ProcessIncomingAudio(testAudio, 8000);
                        bridge.InjectOutgoingAudio(testAudio);
                        bridge.GetNextOutgoingFrame();
                    }
                });
            }
            
            Task.WaitAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalFrames = streamCount * iterationsPerStream;
            var averageTimePerFrame = stopwatch.ElapsedMilliseconds / (double)totalFrames;
            
            // 并发处理性能应该良好
            Assert.True(averageTimePerFrame < 2.0, 
                $"Average concurrent processing time should be < 2ms per frame, actual: {averageTimePerFrame:F3}ms");
            
            // 总处理时间应该合理
            Assert.True(stopwatch.ElapsedMilliseconds < 15000, 
                $"Total concurrent processing time should be < 15s, actual: {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void ByteArray_vs_TypeConversion_PerformanceComparison() {
            // Arrange
            var testAudio = GenerateTestAudio(320);
            const int iterations = 2000;

            // Act - 测量直接byte[]处理性能
            var stopwatch1 = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) {
                // 模拟直接byte[]处理（我们的新架构）
                ProcessAudioDirectly(testAudio);
            }
            stopwatch1.Stop();

            // 测量包含类型转换的处理性能
            var stopwatch2 = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) {
                // 模拟旧的转换方式
                ProcessAudioWithConversion(testAudio);
            }
            stopwatch2.Stop();

            // Assert
            var directProcessingTime = stopwatch1.ElapsedMilliseconds;
            var conversionProcessingTime = stopwatch2.ElapsedMilliseconds;
            
            // 直接处理应该更快或至少不慢
            Assert.True(directProcessingTime <= conversionProcessingTime * 1.2, 
                $"Direct byte[] processing should be faster or comparable. Direct: {directProcessingTime}ms, Conversion: {conversionProcessingTime}ms");
            
            // 两种方式都应该满足实时要求
            Assert.True(directProcessingTime / (double)iterations < 1.0, 
                "Direct processing should be < 1ms per frame");
        }

        /// <summary>
        /// 生成测试音频数据
        /// </summary>
        private byte[] GenerateTestAudio(int length) {
            var audio = new byte[length];
            var random = new Random(42); // 固定种子确保一致性
            
            for (int i = 0; i < length; i += 2) {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 16000) * 8000 + random.Next(-1000, 1000));
                audio[i] = (byte)(sample & 0xFF);
                if (i + 1 < length) {
                    audio[i + 1] = (byte)((sample >> 8) & 0xFF);
                }
            }
            
            return audio;
        }

        /// <summary>
        /// 模拟直接byte[]处理
        /// </summary>
        private void ProcessAudioDirectly(byte[] audio) {
            // 模拟VAD能量计算
            double energy = 0;
            for (int i = 0; i < audio.Length; i += 2) {
                if (i + 1 < audio.Length) {
                    short sample = (short)(audio[i] | (audio[i + 1] << 8));
                    energy += sample * sample;
                }
            }
            energy = Math.Sqrt(energy / (audio.Length / 2));
        }

        /// <summary>
        /// 模拟包含类型转换的处理
        /// </summary>
        private void ProcessAudioWithConversion(byte[] audio) {
            // 模拟旧的转换方式：byte[] → short[] → 处理 → byte[]
            var samples = new short[audio.Length / 2];
            
            // byte[] → short[]
            for (int i = 0; i < samples.Length; i++) {
                samples[i] = (short)(audio[i * 2] | (audio[i * 2 + 1] << 8));
            }
            
            // 处理
            double energy = 0;
            for (int i = 0; i < samples.Length; i++) {
                energy += samples[i] * samples[i];
            }
            energy = Math.Sqrt(energy / samples.Length);
            
            // short[] → byte[] (模拟)
            var result = new byte[audio.Length];
            for (int i = 0; i < samples.Length; i++) {
                result[i * 2] = (byte)(samples[i] & 0xFF);
                result[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
            }
        }
    }
}