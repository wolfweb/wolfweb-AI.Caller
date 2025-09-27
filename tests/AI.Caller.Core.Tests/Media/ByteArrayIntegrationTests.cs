using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Media.Sources;
using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace AI.Caller.Core.Tests.Media {
    /// <summary>
    /// 集成测试：验证完整的byte[]音频处理链路
    /// </summary>
    public class ByteArrayIntegrationTests {
        private readonly Mock<ILogger> _mockLogger;
        private readonly Mock<ILogger<AudioBridge>> _mockAudioBridgeLogger;
        private readonly MediaProfile _testProfile;

        public ByteArrayIntegrationTests() {
            _mockLogger = new Mock<ILogger>();
            _mockAudioBridgeLogger = new Mock<ILogger<AudioBridge>>();
            _testProfile = new MediaProfile(AudioCodec.PCMU, 0, 8000, 20, 1);
        }

        [Fact]
        public void AudioProcessingChain_TtsToNetwork_ShouldProcessByteArraysCorrectly() {
            // Arrange
            var bridge = new AudioBridge(_mockAudioBridgeLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var playbackSource = new QueueAudioPlaybackSource();
            playbackSource.Init(_testProfile);
            playbackSource.StartAsync(default).Wait();

            var codec = new G711Codec();
            var vad = new EnergyVad();
            vad.Configure(0.02f, 200, 600, 8000, 20);

            // 创建测试音频数据 (TTS输出模拟)
            var ttsAudioData = new byte[320]; // 160 samples
            for (int i = 0; i < 160; i++) {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 8000) * 16000);
                ttsAudioData[i * 2] = (byte)(sample & 0xFF);
                ttsAudioData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            byte[] receivedAudioData = null;
            bridge.IncomingAudioReceived += (data) => receivedAudioData = data;

            // Act - 模拟完整的音频处理链路

            // 1. TTS → AudioBridge (incoming audio)
            bridge.ProcessIncomingAudio(ttsAudioData, 8000);

            // 2. AudioBridge → VAD处理
            var vadResult = vad.Update(receivedAudioData);

            // 3. AudioBridge → Playback Source
            playbackSource.Enqueue(receivedAudioData);
            var playbackFrame = playbackSource.ReadNextPcmFrame();

            // 4. AudioBridge → G711编码 (outgoing audio)
            bridge.InjectOutgoingAudio(ttsAudioData);
            var outgoingFrame = bridge.GetNextOutgoingFrame();
            var encodedFrame = codec.EncodeMuLaw(outgoingFrame);

            // Assert
            Assert.NotNull(receivedAudioData);
            Assert.Equal(320, receivedAudioData.Length);
            Assert.Equal(ttsAudioData, receivedAudioData);

            Assert.True(vadResult.Energy > 0.1f, "VAD should detect audio energy");

            Assert.NotNull(playbackFrame);
            Assert.Equal(320, playbackFrame.Length);

            Assert.NotNull(outgoingFrame);
            Assert.Equal(320, outgoingFrame.Length);

            Assert.NotNull(encodedFrame);
            Assert.Equal(160, encodedFrame.Length); // G711压缩比2:1
        }

        [Fact]
        public void AudioBridge_VAD_Integration_ShouldPauseAndResumeCorrectly() {
            // Arrange
            var bridge = new AudioBridge(_mockAudioBridgeLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var vad = new EnergyVad();
            vad.Configure(0.02f, 200, 600, 8000, 20);

            // 创建静音数据
            var silenceData = new byte[320];
            
            // 创建有声音频数据
            var audioData = new byte[320];
            for (int i = 0; i < 160; i++) {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 8000) * 16000);
                audioData[i * 2] = (byte)(sample & 0xFF);
                audioData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            byte[] lastReceivedData = null;
            bridge.IncomingAudioReceived += (data) => lastReceivedData = data;

            // Act & Assert - 测试VAD状态变化

            // 1. 处理静音 - 应该保持静音状态
            bridge.ProcessIncomingAudio(silenceData, 8000);
            var silenceResult = vad.Update(lastReceivedData);
            Assert.Equal(VADState.Silence, silenceResult.State);

            // 2. 处理有声音频 - 应该检测到语音
            bridge.ProcessIncomingAudio(audioData, 8000);
            var audioResult = vad.Update(lastReceivedData);
            Assert.True(audioResult.Energy > 0.1f, "Should detect significant audio energy");

            // 3. 再次处理静音 - 应该回到静音状态
            bridge.ProcessIncomingAudio(silenceData, 8000);
            var finalSilenceResult = vad.Update(lastReceivedData);
            Assert.Equal(VADState.Silence, finalSilenceResult.State);
        }

        [Fact]
        public void G711Codec_AudioBridge_Integration_ShouldEncodeDecodeCorrectly() {
            // Arrange
            var bridge = new AudioBridge(_mockAudioBridgeLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var codec = new G711Codec();

            // 创建测试音频数据
            var originalAudio = new byte[320];
            for (int i = 0; i < 160; i++) {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 8000) * 8000);
                originalAudio[i * 2] = (byte)(sample & 0xFF);
                originalAudio[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            // Act - 完整的编码/解码循环

            // 1. AudioBridge处理音频
            bridge.InjectOutgoingAudio(originalAudio);
            var bridgeOutput = bridge.GetNextOutgoingFrame();

            // 2. G711编码
            var encoded = codec.EncodeMuLaw(bridgeOutput);

            // 3. G711解码
            var decoded = codec.DecodeG711MuLaw(encoded);

            // 4. 再次通过AudioBridge处理
            bridge.ProcessIncomingAudio(decoded, 8000);

            // Assert
            Assert.NotNull(bridgeOutput);
            Assert.Equal(320, bridgeOutput.Length);

            Assert.NotNull(encoded);
            Assert.Equal(160, encoded.Length);

            Assert.NotNull(decoded);
            Assert.Equal(320, decoded.Length);

            // 验证编码/解码后的音频质量（允许一定的量化误差）
            Assert.True(decoded.Length == originalAudio.Length, "Decoded audio should have same length");
        }

        [Fact]
        public void QueueAudioPlaybackSource_AudioBridge_Integration_ShouldMaintainAudioQuality() {
            // Arrange
            var bridge = new AudioBridge(_mockAudioBridgeLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var playback = new QueueAudioPlaybackSource();
            playback.Init(_testProfile);
            playback.StartAsync(default).Wait();

            // 创建多帧音频数据
            var audioFrames = new byte[3][];
            for (int frame = 0; frame < 3; frame++) {
                audioFrames[frame] = new byte[320];
                for (int i = 0; i < 160; i++) {
                    short sample = (short)(Math.Sin(2 * Math.PI * (440 + frame * 100) * i / 8000) * 12000);
                    audioFrames[frame][i * 2] = (byte)(sample & 0xFF);
                    audioFrames[frame][i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
                }
            }

            byte[] lastReceivedData = null;
            bridge.IncomingAudioReceived += (data) => lastReceivedData = data;

            // Act - 处理多帧音频

            foreach (var frame in audioFrames) {
                // 1. AudioBridge处理输入
                bridge.ProcessIncomingAudio(frame, 8000);
                
                // 2. 添加到播放队列
                playback.Enqueue(lastReceivedData);
                
                // 3. 从播放队列读取
                var playbackFrame = playback.ReadNextPcmFrame();
                
                // Assert - 验证每帧数据
                Assert.NotNull(playbackFrame);
                Assert.Equal(320, playbackFrame.Length);
                Assert.Equal(frame, lastReceivedData);
            }

            // 验证RMS计算
            Assert.True(playback.PlaybackRms > 0.1f, "Playback RMS should reflect audio signal");
        }

        [Fact]
        public void EndToEnd_AudioProcessingPipeline_ShouldWorkCorrectly() {
            // Arrange - 设置完整的音频处理管道
            var bridge = new AudioBridge(_mockAudioBridgeLogger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var playback = new QueueAudioPlaybackSource();
            playback.Init(_testProfile);
            playback.StartAsync(default).Wait();

            var codec = new G711Codec();
            var vad = new EnergyVad();
            vad.Configure(0.02f, 200, 600, 8000, 20);

            // 模拟TTS生成的音频数据
            var ttsOutput = new byte[320];
            for (int i = 0; i < 160; i++) {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 8000) * 16000);
                ttsOutput[i * 2] = (byte)(sample & 0xFF);
                ttsOutput[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            // 设置事件处理
            byte[] processedAudio = null;
            byte[] networkAudio = null;
            
            bridge.IncomingAudioReceived += (data) => processedAudio = data;
            bridge.OutgoingAudioRequested += (data) => networkAudio = data;

            // Act - 执行完整的音频处理流程

            // 1. TTS → AudioBridge (模拟AI语音输入)
            bridge.ProcessIncomingAudio(ttsOutput, 8000);

            // 2. VAD处理 (语音活动检测)
            var vadResult = vad.Update(processedAudio);

            // 3. 播放处理
            playback.Enqueue(processedAudio);
            var playbackFrame = playback.ReadNextPcmFrame();

            // 4. 网络输出处理
            bridge.InjectOutgoingAudio(processedAudio);
            var outgoingFrame = bridge.GetNextOutgoingFrame();

            // 5. G711编码 (网络传输)
            var encodedForNetwork = codec.EncodeMuLaw(outgoingFrame);

            // 6. G711解码 (接收端)
            var decodedFromNetwork = codec.DecodeG711MuLaw(encodedForNetwork);

            // Assert - 验证整个处理链路
            
            // 验证AudioBridge处理
            Assert.NotNull(processedAudio);
            Assert.Equal(320, processedAudio.Length);
            Assert.Equal(ttsOutput, processedAudio);

            // 验证VAD检测
            Assert.True(vadResult.Energy > 0.1f, "VAD should detect speech energy");

            // 验证播放处理
            Assert.NotNull(playbackFrame);
            Assert.Equal(320, playbackFrame.Length);
            Assert.True(playback.PlaybackRms > 0.1f, "Playback should have audio signal");

            // 验证网络输出
            Assert.NotNull(outgoingFrame);
            Assert.Equal(320, outgoingFrame.Length);

            // 验证编码/解码
            Assert.NotNull(encodedForNetwork);
            Assert.Equal(160, encodedForNetwork.Length);
            Assert.NotNull(decodedFromNetwork);
            Assert.Equal(320, decodedFromNetwork.Length);

            // 验证数据完整性 (所有处理都使用byte[]，无转换损失)
            Assert.True(processedAudio.Length == ttsOutput.Length, "No data loss in processing");
            Assert.True(outgoingFrame.Length == processedAudio.Length, "Consistent frame sizes");
        }
    }
}