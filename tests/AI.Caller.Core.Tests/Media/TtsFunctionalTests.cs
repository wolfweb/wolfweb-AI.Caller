using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Media.Sources;
using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Linq;
using Xunit;

namespace AI.Caller.Core.Tests.Media {
    /// <summary>
    /// 功能验证测试：验证AI TTS音频输出正常，不再静音
    /// </summary>
    public class TtsFunctionalTests {
        private readonly Mock<ILogger<AudioBridge>> _mockLogger;
        private readonly MediaProfile _testProfile;

        public TtsFunctionalTests() {
            _mockLogger = new Mock<ILogger<AudioBridge>>();
            _testProfile = new MediaProfile(AudioCodec.PCMU, 0, 8000, 20, 1);
        }

        [Fact]
        public void TtsAudio_ShouldNotBeSilent_AfterByteArrayFix() {
            // Arrange
            var bridge = new AudioBridge(_mockLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var vad = new EnergyVad();
            vad.Configure(0.02f, 200, 600, 8000, 20);

            // 模拟AI TTS生成的音频数据（非静音）
            var ttsAudioData = GenerateTestTtsAudio(440, 0.8f); // 440Hz正弦波，80%音量

            byte[] receivedAudio = null;
            bridge.IncomingAudioReceived += (data) => receivedAudio = data;

            // Act
            bridge.ProcessIncomingAudio(ttsAudioData, 8000);
            var vadResult = vad.Update(receivedAudio);

            // Assert
            Assert.NotNull(receivedAudio);
            Assert.Equal(320, receivedAudio.Length);
            
            // 验证音频不是静音
            Assert.True(vadResult.Energy > 0.1f, $"TTS audio should not be silent. Energy: {vadResult.Energy}");
            
            // 验证音频数据包含非零值
            var nonZeroSamples = CountNonZeroSamples(receivedAudio);
            Assert.True(nonZeroSamples > 100, $"TTS audio should contain significant non-zero samples. Found: {nonZeroSamples}");
        }

        [Fact]
        public void TtsAudio_QualityVerification_ShouldHaveGoodSignalToNoiseRatio() {
            // Arrange
            var bridge = new AudioBridge(_mockLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var playback = new QueueAudioPlaybackSource();
            playback.Init(_testProfile);
            playback.StartAsync(default).Wait();

            // 生成高质量TTS音频数据
            var highQualityTts = GenerateTestTtsAudio(440, 0.9f);
            var lowQualityTts = GenerateTestTtsAudio(440, 0.1f);

            byte[] highQualityReceived = null;
            byte[] lowQualityReceived = null;

            bridge.IncomingAudioReceived += (data) => {
                if (highQualityReceived == null)
                    highQualityReceived = data;
                else
                    lowQualityReceived = data;
            };

            // Act
            bridge.ProcessIncomingAudio(highQualityTts, 8000);
            playback.Enqueue(highQualityReceived);
            playback.ReadNextPcmFrame();
            var highQualityRms = playback.PlaybackRms;

            bridge.ProcessIncomingAudio(lowQualityTts, 8000);
            playback.Enqueue(lowQualityReceived);
            playback.ReadNextPcmFrame();
            var lowQualityRms = playback.PlaybackRms;

            // Assert
            Assert.True(highQualityRms > lowQualityRms, 
                $"High quality TTS should have higher RMS. High: {highQualityRms}, Low: {lowQualityRms}");
            Assert.True(highQualityRms > 0.3f, 
                $"High quality TTS should have significant RMS value: {highQualityRms}");
        }

        [Fact]
        public void TtsAudio_DifferentSampleRates_ShouldBeHandledCorrectly() {
            // Arrange
            var bridge = new AudioBridge(_mockLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var vad = new EnergyVad();
            vad.Configure(0.02f, 200, 600, 8000, 20);

            // 测试不同采样率的TTS音频
            var audio8kHz = GenerateTestTtsAudio(440, 0.8f);
            var audio16kHz = GenerateTestTtsAudio(440, 0.8f, 16000);

            byte[] received8k = null;
            byte[] received16k = null;

            bridge.IncomingAudioReceived += (data) => {
                if (received8k == null)
                    received8k = data;
                else
                    received16k = data;
            };

            // Act
            bridge.ProcessIncomingAudio(audio8kHz, 8000);
            var vad8kResult = vad.Update(received8k);

            bridge.ProcessIncomingAudio(audio16kHz, 16000);
            var vad16kResult = vad.Update(received16k);

            // Assert
            Assert.True(vad8kResult.Energy > 0.1f, 
                $"8kHz TTS audio should be detected. Energy: {vad8kResult.Energy}");
            Assert.True(vad16kResult.Energy > 0.1f, 
                $"16kHz TTS audio should be detected. Energy: {vad16kResult.Energy}");
            
            // 验证重采样后的音频质量
            Assert.NotNull(received8k);
            Assert.NotNull(received16k);
            Assert.Equal(320, received8k.Length); // 应该都是8kHz输出
            Assert.Equal(320, received16k.Length);
        }

        [Fact]
        public void TtsAudio_G711Encoding_ShouldPreserveAudioContent() {
            // Arrange
            var bridge = new AudioBridge(_mockLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var codec = new G711Codec();
            var vad = new EnergyVad();
            vad.Configure(0.02f, 200, 600, 8000, 20);

            // 生成TTS音频
            var ttsAudio = GenerateTestTtsAudio(440, 0.8f);

            byte[] bridgeOutput = null;
            bridge.IncomingAudioReceived += (data) => bridgeOutput = data;

            // Act
            // 1. TTS → AudioBridge
            bridge.ProcessIncomingAudio(ttsAudio, 8000);
            
            // 2. AudioBridge → G711编码
            bridge.InjectOutgoingAudio(bridgeOutput);
            var outgoingFrame = bridge.GetNextOutgoingFrame();
            var encoded = codec.EncodeMuLaw(outgoingFrame);
            
            // 3. G711解码
            var decoded = codec.DecodeG711MuLaw(encoded);
            
            // 4. VAD验证解码后的音频
            var vadResult = vad.Update(decoded);

            // Assert
            Assert.NotNull(encoded);
            Assert.Equal(160, encoded.Length);
            
            Assert.NotNull(decoded);
            Assert.Equal(320, decoded.Length);
            
            // 验证编码/解码后音频仍然可检测
            Assert.True(vadResult.Energy > 0.05f, 
                $"Encoded/decoded TTS audio should still be detectable. Energy: {vadResult.Energy}");
            
            // 验证编码后的数据不全为零
            var nonZeroEncoded = encoded.Count(b => b != 0);
            Assert.True(nonZeroEncoded > 50, 
                $"Encoded audio should contain non-zero values. Found: {nonZeroEncoded}");
        }

        [Fact]
        public void TtsAudio_WebToServerCall_ShouldTransmitCorrectly() {
            // Arrange - 模拟WebToServer通话场景
            var clientBridge = new AudioBridge(_mockLogger.Object);
            var serverBridge = new AudioBridge(_mockLogger.Object);
            
            clientBridge.Initialize(_testProfile);
            serverBridge.Initialize(_testProfile);
            clientBridge.Start();
            serverBridge.Start();

            var codec = new G711Codec();
            var serverVad = new EnergyVad();
            serverVad.Configure(0.02f, 200, 600, 8000, 20);

            // 模拟客户端TTS音频
            var clientTtsAudio = GenerateTestTtsAudio(440, 0.8f);

            byte[] clientOutput = null;
            byte[] serverReceived = null;

            clientBridge.IncomingAudioReceived += (data) => clientOutput = data;
            serverBridge.IncomingAudioReceived += (data) => serverReceived = data;

            // Act - 模拟完整的WebToServer通话流程
            
            // 1. 客户端：TTS → AudioBridge
            clientBridge.ProcessIncomingAudio(clientTtsAudio, 8000);
            
            // 2. 客户端：AudioBridge → 网络发送
            clientBridge.InjectOutgoingAudio(clientOutput);
            var clientOutgoing = clientBridge.GetNextOutgoingFrame();
            var networkPacket = codec.EncodeMuLaw(clientOutgoing);
            
            // 3. 网络传输（模拟）
            var receivedPacket = networkPacket; // 假设无损传输
            
            // 4. 服务端：网络接收 → 解码
            var serverDecoded = codec.DecodeG711MuLaw(receivedPacket);
            
            // 5. 服务端：AudioBridge处理
            serverBridge.ProcessIncomingAudio(serverDecoded, 8000);
            
            // 6. 服务端：VAD检测
            var serverVadResult = serverVad.Update(serverReceived);

            // Assert
            Assert.NotNull(clientOutput);
            Assert.NotNull(clientOutgoing);
            Assert.NotNull(networkPacket);
            Assert.NotNull(serverDecoded);
            Assert.NotNull(serverReceived);
            
            // 验证服务端能够检测到客户端的TTS音频
            Assert.True(serverVadResult.Energy > 0.05f, 
                $"Server should detect client TTS audio. Energy: {serverVadResult.Energy}");
            
            // 验证数据完整性
            Assert.Equal(320, clientOutput.Length);
            Assert.Equal(320, clientOutgoing.Length);
            Assert.Equal(160, networkPacket.Length);
            Assert.Equal(320, serverDecoded.Length);
            Assert.Equal(320, serverReceived.Length);
        }

        [Fact]
        public void TtsAudio_VadPauseResume_ShouldWorkCorrectly() {
            // Arrange
            var bridge = new AudioBridge(_mockLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var vad = new EnergyVad();
            vad.Configure(0.02f, 200, 600, 8000, 20);

            // 生成音频序列：静音 → TTS → 静音
            var silenceAudio = new byte[320]; // 全零静音
            var ttsAudio = GenerateTestTtsAudio(440, 0.8f);

            byte[] receivedAudio = null;
            bridge.IncomingAudioReceived += (data) => receivedAudio = data;

            // Act & Assert - 测试VAD状态变化

            // 1. 初始静音
            bridge.ProcessIncomingAudio(silenceAudio, 8000);
            var silenceResult1 = vad.Update(receivedAudio);
            Assert.Equal(VADState.Silence, silenceResult1.State);

            // 2. TTS音频开始
            bridge.ProcessIncomingAudio(ttsAudio, 8000);
            var ttsResult = vad.Update(receivedAudio);
            Assert.True(ttsResult.Energy > 0.1f, "TTS should be detected");

            // 3. 返回静音
            bridge.ProcessIncomingAudio(silenceAudio, 8000);
            var silenceResult2 = vad.Update(receivedAudio);
            Assert.Equal(VADState.Silence, silenceResult2.State);

            // 验证VAD能够正确检测TTS音频的开始和结束
            Assert.True(ttsResult.Energy > silenceResult1.Energy, "TTS energy should be higher than silence");
            Assert.True(ttsResult.Energy > silenceResult2.Energy, "TTS energy should be higher than silence");
        }

        /// <summary>
        /// 生成测试用的TTS音频数据
        /// </summary>
        private byte[] GenerateTestTtsAudio(double frequency, float amplitude, int sampleRate = 8000) {
            var samples = sampleRate * 20 / 1000; // 20ms帧
            var audioData = new byte[samples * 2]; // 16位PCM

            for (int i = 0; i < samples; i++) {
                var sample = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * amplitude * 16000);
                audioData[i * 2] = (byte)(sample & 0xFF);
                audioData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            return audioData;
        }

        /// <summary>
        /// 计算音频数据中非零样本的数量
        /// </summary>
        private int CountNonZeroSamples(byte[] audioData) {
            int count = 0;
            for (int i = 0; i < audioData.Length; i += 2) {
                if (i + 1 < audioData.Length) {
                    short sample = (short)(audioData[i] | (audioData[i + 1] << 8));
                    if (sample != 0) count++;
                }
            }
            return count;
        }
    }
}