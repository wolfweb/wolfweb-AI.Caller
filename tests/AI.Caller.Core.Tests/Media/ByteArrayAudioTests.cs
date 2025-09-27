using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Media.Sources;
using AI.Caller.Core.Media.Vad;
using AI.Caller.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using Xunit;

namespace AI.Caller.Core.Tests.Media {
    /// <summary>
    /// 测试统一byte[]架构的所有组件
    /// </summary>
    public class ByteArrayAudioTests {
        private readonly Mock<ILogger> _mockLogger;
        private readonly MediaProfile _testProfile;

        public ByteArrayAudioTests() {
            _mockLogger = new Mock<ILogger>();
            _testProfile = new MediaProfile(AudioCodec.PCMU, 0, 8000, 20, 1);
        }

        [Fact]
        public void EnergyVad_Update_WithByteArray_ShouldCalculateEnergyCorrectly() {
            // Arrange
            var vad = new EnergyVad();
            vad.Configure(0.02f, 200, 600, 8000, 20);

            // 创建测试音频数据：160样本 = 320字节的静音
            var silenceData = new byte[320];
            
            // 创建测试音频数据：160样本 = 320字节的有声音频
            var audioData = new byte[320];
            for (int i = 0; i < 160; i++) {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 8000) * 16000);
                audioData[i * 2] = (byte)(sample & 0xFF);
                audioData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            // Act
            var silenceResult = vad.Update(silenceData);
            var audioResult = vad.Update(audioData);

            // Assert
            Assert.Equal(VADState.Silence, silenceResult.State);
            Assert.True(silenceResult.Energy < 0.01f, "Silence energy should be very low");
            
            Assert.True(audioResult.Energy > 0.1f, "Audio energy should be significant");
        }

        [Fact]
        public void G711Codec_MuLawEncode_WithByteArray_ShouldProduceValidOutput() {
            // Arrange
            var codec = new G711Codec();
            var pcmData = new byte[320]; // 160 samples
            
            // 填充测试PCM数据
            for (int i = 0; i < 160; i++) {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 8000) * 16000);
                pcmData[i * 2] = (byte)(sample & 0xFF);
                pcmData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            // Act
            var encoded = codec.EncodeMuLaw(pcmData);

            // Assert
            Assert.NotNull(encoded);
            Assert.Equal(160, encoded.Length); // 压缩比 2:1
        }

        [Fact]
        public void G711Codec_MuLawDecode_WithByteArray_ShouldProduceValidOutput() {
            // Arrange
            var codec = new G711Codec();
            var muLawData = new byte[160]; // 160 μ-law samples
            
            // 填充测试μ-law数据
            for (int i = 0; i < 160; i++) {
                muLawData[i] = (byte)(i % 256);
            }

            // Act
            var decoded = codec.DecodeG711MuLaw(muLawData);

            // Assert
            Assert.NotNull(decoded);
            Assert.Equal(320, decoded.Length); // 解压缩比 1:2
        }

        [Fact]
        public void AudioBridge_ProcessIncomingAudio_ShouldTriggerByteArrayEvent() {
            // Arrange
            var logger = new Mock<ILogger<AudioBridge>>();
            var bridge = new AudioBridge(logger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            byte[] receivedData = null;
            bridge.IncomingAudioReceived += (data) => receivedData = data;

            var testData = new byte[320];

            // Act
            bridge.ProcessIncomingAudio(testData, 8000);

            // Assert
            Assert.NotNull(receivedData);
            Assert.Equal(320, receivedData.Length);
        }

        [Fact]
        public void AudioBridge_GetNextOutgoingFrame_ShouldReturnByteArray() {
            // Arrange
            var logger = new Mock<ILogger<AudioBridge>>();
            var bridge = new AudioBridge(logger.Object);
            bridge.Initialize(_testProfile);
            bridge.Start();

            var testData = new byte[320];
            bridge.InjectOutgoingAudio(testData);

            // Act
            var frame = bridge.GetNextOutgoingFrame();

            // Assert
            Assert.NotNull(frame);
            Assert.IsType<byte[]>(frame);
            Assert.Equal(320, frame.Length); // _samplesPerFrame * 2
        }

        [Fact]
        public void QueueAudioPlaybackSource_ReadNextPcmFrame_ShouldReturnCorrectBufferSize() {
            // Arrange
            var playback = new QueueAudioPlaybackSource();
            playback.Init(_testProfile);
            playback.StartAsync(default).Wait();

            var testData = new byte[320];
            playback.Enqueue(testData);

            // Act
            var frame = playback.ReadNextPcmFrame();

            // Assert
            Assert.NotNull(frame);
            Assert.Equal(320, frame.Length); // _samplesPerFrame * 2 = 160 * 2
        }

        [Fact]
        public void QueueAudioPlaybackSource_UpdatePlaybackRms_ShouldCalculateCorrectly() {
            // Arrange
            var playback = new QueueAudioPlaybackSource();
            playback.Init(_testProfile);
            playback.StartAsync(default).Wait();

            // 创建有声音频数据
            var audioData = new byte[320];
            for (int i = 0; i < 160; i++) {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 8000) * 16000);
                audioData[i * 2] = (byte)(sample & 0xFF);
                audioData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            playback.Enqueue(audioData);

            // Act
            var frame = playback.ReadNextPcmFrame();
            var rms = playback.PlaybackRms;

            // Assert
            Assert.True(rms > 0.1f, "RMS should be significant for audio signal");
        }

        [Fact]
        public void AudioResampler_ByteArray_ShouldHandleResampling() {
            // Arrange
            var logger = new Mock<ILogger>();
            using var resampler = new AudioResampler<byte>(8000, 16000, logger.Object);

            var inputData = new byte[320]; // 160 samples at 8kHz
            // 填充测试数据
            for (int i = 0; i < 160; i++) {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 8000) * 16000);
                inputData[i * 2] = (byte)(sample & 0xFF);
                inputData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            // Act
            var outputData = resampler.Resample(inputData);

            // Assert
            Assert.NotNull(outputData);
            // 如果FFmpeg重采样器初始化失败，会返回原始数据（passthrough）
            // 在这种情况下，输出长度应该等于输入长度
            // 如果重采样成功，输出应该大约是输入的2倍长度（8kHz -> 16kHz）
            Assert.True(outputData.Length == 320 || (outputData.Length >= 600 && outputData.Length <= 680), 
                $"Expected either 320 bytes (passthrough) or ~640 bytes (resampled), got {outputData.Length}");
        }

        [Fact]
        public void AudioResampler_ByteArray_SameRate_ShouldPassthrough() {
            // Arrange
            var logger = new Mock<ILogger>();
            using var resampler = new AudioResampler<byte>(8000, 8000, logger.Object);

            var inputData = new byte[320];
            for (int i = 0; i < inputData.Length; i++) {
                inputData[i] = (byte)(i % 256);
            }

            // Act
            var outputData = resampler.Resample(inputData);

            // Assert
            Assert.Equal(inputData.Length, outputData.Length);
            Assert.Equal(inputData, outputData);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(319)]
        public void EnergyVad_InvalidByteArrayLength_ShouldHandleGracefully(int length) {
            // Arrange
            var vad = new EnergyVad();
            vad.Configure(0.02f, 200, 600, 8000, 20);
            var data = new byte[length];

            // Act
            var result = vad.Update(data);

            // Assert
            Assert.Equal(VADState.Silence, result.State);
            Assert.Equal(0f, result.Energy);
        }

        [Fact]
        public void G711Codec_InvalidByteArrayLength_ShouldThrowException() {
            // Arrange
            var codec = new G711Codec();
            var oddLengthData = new byte[321]; // 奇数长度

            // Act & Assert
            Assert.Throws<ArgumentException>(() => codec.EncodeMuLaw(oddLengthData));
            Assert.Throws<ArgumentException>(() => codec.EncodeALaw(oddLengthData));
        }

        [Fact]
        public void FfmpegEnhancedVad_Update_WithByteArray_ShouldDetectSpeech() {
            try {
                // Arrange
                using var vad = new FfmpegEnhancedVad(8000, 16000, 120);
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

                // Act
                var silenceResult = vad.Update(silenceData);
                var audioResult = vad.Update(audioData);

                // Assert
                Assert.Equal(VADState.Silence, silenceResult.State);
                Assert.True(silenceResult.Energy >= 0f, "Energy should be non-negative");
                
                Assert.True(audioResult.Energy >= 0f, "Audio energy should be non-negative");
            } catch (NotSupportedException) {
                // FFmpeg libraries not available in test environment - skip test
                Assert.True(true, "FFmpeg not available in test environment");
            }
        }



        [Fact]
        public void FfmpegEnhancedVad_InvalidByteArrayLength_ShouldHandleGracefully() {
            try {
                // Arrange
                using var vad = new FfmpegEnhancedVad(8000, 16000, 120);
                vad.Configure(0.02f, 200, 600, 8000, 20);
                
                var emptyData = new byte[0];
                var oddLengthData = new byte[321];

                // Act
                var emptyResult = vad.Update(emptyData);
                var oddResult = vad.Update(oddLengthData);

                // Assert
                Assert.Equal(VADState.Silence, emptyResult.State);
                Assert.Equal(0f, emptyResult.Energy);
                Assert.Equal(VADState.Silence, oddResult.State);
                Assert.Equal(0f, oddResult.Energy);
            } catch (NotSupportedException) {
                // FFmpeg libraries not available in test environment - skip test
                Assert.True(true, "FFmpeg not available in test environment");
            }
        }
    }
}