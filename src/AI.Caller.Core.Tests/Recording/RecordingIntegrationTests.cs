using AI.Caller.Core.Recording;
using Microsoft.Extensions.Logging;
using Moq;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Net;
using Xunit;
using AudioFormat = AI.Caller.Core.Recording.AudioFormat;

namespace AI.Caller.Core.Tests.Recording
{
    public class RecordingIntegrationTests : IDisposable
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly string _testOutputDir;
        
        public RecordingIntegrationTests()
        {
            _mockLogger = new Mock<ILogger>();
            _testOutputDir = Path.Combine(Path.GetTempPath(), "RecordingIntegrationTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testOutputDir);
        }
        
        [Fact]
        public async Task CompleteRecordingWorkflow_ShouldCreateHighQualityRecording()
        {
            // Arrange - 模拟完整的录音工作流程
            var recordingOptions = new RecordingOptions
            {
                Codec = AudioCodec.PCM_WAV,
                SampleRate = 44100,
                Channels = 2,
                BitRate = 128000,
                OutputDirectory = _testOutputDir,
                FileNameTemplate = "complete_workflow_test",
                RecordBothParties = true
            };
            
            var encodingOptions = new AudioEncodingOptions
            {
                Codec = recordingOptions.Codec,
                SampleRate = recordingOptions.SampleRate,
                Channels = recordingOptions.Channels,
                BitRate = recordingOptions.BitRate,
                Quality = AudioQuality.High
            };
            
            // 创建组件
            var audioRecorder = new AudioRecorder(_mockLogger.Object);
            var audioMixer = new AudioMixer(_mockLogger.Object);
            var audioEncoder = new FFmpegAudioEncoder(encodingOptions, _mockLogger.Object);
            var formatConverter = new AudioFormatConverter(_mockLogger.Object);
            
            var outputPath = Path.Combine(_testOutputDir, "complete_workflow_test.wav");
            var audioFormat = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            // Act - 执行完整的录音流程
            
            // 1. 初始化编码器
            var initResult = await audioEncoder.InitializeAsync(audioFormat, outputPath);
            Assert.True(initResult);
            
            // 2. 开始录音
            await audioRecorder.StartCaptureAsync(AudioSource.RTP_Incoming, AudioSource.RTP_Outgoing);
            Assert.True(audioRecorder.IsCapturing);
            
            // 3. 模拟通话音频数据（5秒通话）
            var callDuration = TimeSpan.FromSeconds(5);
            var frameCount = (int)(callDuration.TotalSeconds * 50); // 50 frames per second (20ms each)
            var samplesPerFrame = audioFormat.SampleRate / 50; // 20ms worth of samples
            
            var mixedFrames = new List<AudioFrame>();
            
            for (int i = 0; i < frameCount; i++)
            {
                var timeOffset = i * 0.02; // 20ms per frame
                
                // 模拟来话方音频（较低频率，模拟男声）
                var incomingAudio = GenerateRealisticVoiceAudio(samplesPerFrame, audioFormat.SampleRate, 
                    baseFreq: 120 + 20 * Math.Sin(timeOffset * 2), // 变化的基频
                    amplitude: 0.6 + 0.2 * Math.Sin(timeOffset * 3)); // 变化的音量
                var incomingFrame = new AudioFrame(incomingAudio, audioFormat, AudioSource.RTP_Incoming);
                
                // 模拟去话方音频（较高频率，模拟女声）
                var outgoingAudio = GenerateRealisticVoiceAudio(samplesPerFrame, audioFormat.SampleRate,
                    baseFreq: 200 + 30 * Math.Sin(timeOffset * 1.5), // 不同的变化模式
                    amplitude: 0.5 + 0.3 * Math.Sin(timeOffset * 2.5));
                var outgoingFrame = new AudioFrame(outgoingAudio, audioFormat, AudioSource.RTP_Outgoing);
                
                // 4. 混合双方音频
                var mixedFrame = audioMixer.MixFrames(incomingFrame, outgoingFrame);
                Assert.NotNull(mixedFrame);
                mixedFrames.Add(mixedFrame);
                
                // 5. 编码音频帧
                var encodeResult = await audioEncoder.EncodeAudioFrameAsync(mixedFrame);
                Assert.True(encodeResult);
                
                // 模拟实时处理延迟
                if (i % 10 == 0) // 每200ms输出一次进度
                {
                    Console.WriteLine($"Processing frame {i + 1}/{frameCount} ({(i + 1) * 100.0 / frameCount:F1}%)");
                }
            }
            
            // 6. 完成录音
            await audioRecorder.StopCaptureAsync();
            Assert.False(audioRecorder.IsCapturing);
            
            var finalizeResult = await audioEncoder.FinalizeAsync();
            Assert.True(finalizeResult);
            
            // Assert - 验证结果
            Assert.True(File.Exists(outputPath));
            var fileInfo = new FileInfo(outputPath);
            
            // 验证文件大小合理（5秒44.1kHz立体声16位音频约为882KB + WAV头）
            var expectedSize = 44 + (44100 * 2 * 2 * 5); // Header + (SampleRate * Channels * BytesPerSample * Seconds)
            Assert.True(fileInfo.Length >= expectedSize * 0.9); // 允许10%的误差
            Assert.True(fileInfo.Length <= expectedSize * 1.1);
            
            // 验证WAV文件结构
            await VerifyWavFileStructure(outputPath, audioFormat);
            
            Console.WriteLine($"\n✅ Complete recording workflow test passed!");
            Console.WriteLine($"📁 Generated file: {outputPath}");
            Console.WriteLine($"📊 File size: {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0:F1} KB)");
            Console.WriteLine($"⏱️  Duration: ~{callDuration.TotalSeconds} seconds");
            Console.WriteLine($"🎵 Format: {audioFormat.SampleRate}Hz, {audioFormat.Channels}ch, {audioFormat.BitsPerSample}bit");
            Console.WriteLine($"🔊 Frames processed: {mixedFrames.Count}");
            
            // Cleanup
            audioRecorder.Dispose();
            audioMixer.Dispose();
            audioEncoder.Dispose();
            formatConverter.Dispose();
        }
        
        [Fact]
        public async Task TestDifferentAudioFormats_ShouldGenerateMultipleFormats()
        {
            // Test different audio format combinations
            var testCases = new[]
            {
                new { Name = "telephone_quality", SampleRate = 8000, Channels = 1, Description = "Telephone Quality (8kHz Mono)" },
                new { Name = "cd_quality", SampleRate = 44100, Channels = 2, Description = "CD Quality (44.1kHz Stereo)" },
                new { Name = "high_quality", SampleRate = 48000, Channels = 2, Description = "High Quality (48kHz Stereo)" },
                new { Name = "broadcast_quality", SampleRate = 22050, Channels = 1, Description = "Broadcast Quality (22kHz Mono)" }
            };
            
            foreach (var testCase in testCases)
            {
                Console.WriteLine($"\n🎵 Testing {testCase.Description}...");
                
                var audioFormat = new AudioFormat(testCase.SampleRate, testCase.Channels, 16, AudioSampleFormat.PCM);
                var encodingOptions = new AudioEncodingOptions
                {
                    Codec = AudioCodec.PCM_WAV,
                    SampleRate = testCase.SampleRate,
                    Channels = testCase.Channels,
                    BitRate = 64000
                };
                
                var encoder = new FFmpegAudioEncoder(encodingOptions, _mockLogger.Object);
                var outputPath = Path.Combine(_testOutputDir, $"{testCase.Name}.wav");
                
                await encoder.InitializeAsync(audioFormat, outputPath);
                
                // Generate 2 seconds of test audio
                var duration = TimeSpan.FromSeconds(2);
                var totalSamples = (int)(audioFormat.SampleRate * duration.TotalSeconds);
                var audioData = GenerateTestTone(totalSamples, audioFormat.SampleRate, audioFormat.Channels);
                
                var audioFrame = new AudioFrame(audioData, audioFormat, AudioSource.Mixed);
                await encoder.EncodeAudioFrameAsync(audioFrame);
                await encoder.FinalizeAsync();
                
                // Verify
                Assert.True(File.Exists(outputPath));
                var fileInfo = new FileInfo(outputPath);
                Console.WriteLine($"   ✅ Generated: {fileInfo.Length:N0} bytes");
                
                encoder.Dispose();
            }
        }
        
        [Fact]
        public async Task TestAudioQualityLevels_ShouldGenerateDifferentQualityFiles()
        {
            var qualityLevels = new[]
            {
                AudioEncodingOptions.CreateLowQuality(),
                AudioEncodingOptions.CreateDefault(),
                AudioEncodingOptions.CreateHighQuality()
            };
            
            var qualityNames = new[] { "low_quality", "standard_quality", "high_quality" };
            
            for (int i = 0; i < qualityLevels.Length; i++)
            {
                var options = qualityLevels[i];
                var name = qualityNames[i];
                
                Console.WriteLine($"\n🎚️  Testing {name}: {options.Codec}, {options.SampleRate}Hz, {options.Channels}ch, {options.BitRate}bps");
                
                var audioFormat = new AudioFormat(options.SampleRate, options.Channels, 16, AudioSampleFormat.PCM);
                var encoder = new FFmpegAudioEncoder(options, _mockLogger.Object);
                var outputPath = Path.Combine(_testOutputDir, $"{name}.wav");
                
                await encoder.InitializeAsync(audioFormat, outputPath);
                
                // Generate 3 seconds of complex audio
                var duration = TimeSpan.FromSeconds(3);
                var totalSamples = (int)(audioFormat.SampleRate * duration.TotalSeconds);
                var audioData = GenerateComplexTestAudio(totalSamples, audioFormat.SampleRate, audioFormat.Channels);
                
                var audioFrame = new AudioFrame(audioData, audioFormat, AudioSource.Mixed);
                await encoder.EncodeAudioFrameAsync(audioFrame);
                await encoder.FinalizeAsync();
                
                // Verify
                Assert.True(File.Exists(outputPath));
                var fileInfo = new FileInfo(outputPath);
                Console.WriteLine($"   ✅ Generated: {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0:F1} KB)");
                
                encoder.Dispose();
            }
        }
        
        /// <summary>
        /// Generate realistic voice-like audio with harmonics and formants
        /// </summary>
        private byte[] GenerateRealisticVoiceAudio(int sampleCount, int sampleRate, double baseFreq, double amplitude)
        {
            var audioData = new byte[sampleCount * 4]; // 16-bit stereo
            var random = new Random();
            
            for (int i = 0; i < sampleCount; i++)
            {
                var time = (double)i / sampleRate;
                
                // Fundamental frequency with slight vibrato
                var fundamental = baseFreq * (1 + 0.02 * Math.Sin(2 * Math.PI * 5 * time));
                
                // Voice-like harmonics
                var h1 = Math.Sin(2 * Math.PI * fundamental * time) * 1.0;
                var h2 = Math.Sin(2 * Math.PI * fundamental * 2 * time) * 0.5;
                var h3 = Math.Sin(2 * Math.PI * fundamental * 3 * time) * 0.25;
                var h4 = Math.Sin(2 * Math.PI * fundamental * 4 * time) * 0.125;
                
                // Add formant-like filtering (simplified)
                var formant1 = Math.Sin(2 * Math.PI * 800 * time) * 0.3;
                var formant2 = Math.Sin(2 * Math.PI * 1200 * time) * 0.2;
                
                // Combine harmonics and formants
                var signal = (h1 + h2 + h3 + h4) * 0.7 + (formant1 + formant2) * 0.3;
                
                // Add slight noise for realism
                var noise = (random.NextDouble() - 0.5) * 0.05;
                
                // Apply amplitude envelope
                var envelope = amplitude * (0.8 + 0.2 * Math.Sin(2 * Math.PI * 3 * time));
                
                var finalSignal = (signal + noise) * envelope;
                var sample = (short)(finalSignal * 12000);
                
                // Stereo: slightly different in each channel
                var leftSample = sample;
                var rightSample = (short)(sample * 0.95);
                
                var leftBytes = BitConverter.GetBytes(leftSample);
                var rightBytes = BitConverter.GetBytes(rightSample);
                
                audioData[i * 4] = leftBytes[0];
                audioData[i * 4 + 1] = leftBytes[1];
                audioData[i * 4 + 2] = rightBytes[0];
                audioData[i * 4 + 3] = rightBytes[1];
            }
            
            return audioData;
        }
        
        /// <summary>
        /// Generate simple test tone
        /// </summary>
        private byte[] GenerateTestTone(int sampleCount, int sampleRate, int channels)
        {
            var bytesPerSample = 2; // 16-bit
            var audioData = new byte[sampleCount * channels * bytesPerSample];
            var frequency = 440.0; // A4 note
            
            for (int i = 0; i < sampleCount; i++)
            {
                var time = (double)i / sampleRate;
                var amplitude = Math.Sin(2 * Math.PI * frequency * time);
                var sample = (short)(amplitude * 16000);
                
                for (int ch = 0; ch < channels; ch++)
                {
                    var offset = (i * channels + ch) * bytesPerSample;
                    var bytes = BitConverter.GetBytes(sample);
                    audioData[offset] = bytes[0];
                    audioData[offset + 1] = bytes[1];
                }
            }
            
            return audioData;
        }
        
        /// <summary>
        /// Generate complex test audio with multiple frequency components
        /// </summary>
        private byte[] GenerateComplexTestAudio(int sampleCount, int sampleRate, int channels)
        {
            var bytesPerSample = 2; // 16-bit
            var audioData = new byte[sampleCount * channels * bytesPerSample];
            var random = new Random(123); // Fixed seed for reproducible tests
            
            for (int i = 0; i < sampleCount; i++)
            {
                var time = (double)i / sampleRate;
                
                // Multiple frequency components
                var freq1 = 220 * Math.Sin(2 * Math.PI * 0.5 * time); // Varying frequency
                var freq2 = 440 + 100 * Math.Sin(2 * Math.PI * 1.5 * time);
                var freq3 = 880;
                
                var signal1 = Math.Sin(2 * Math.PI * freq1 * time) * 0.4;
                var signal2 = Math.Sin(2 * Math.PI * freq2 * time) * 0.3;
                var signal3 = Math.Sin(2 * Math.PI * freq3 * time) * 0.2;
                
                // Add some noise
                var noise = (random.NextDouble() - 0.5) * 0.1;
                
                var combinedSignal = signal1 + signal2 + signal3 + noise;
                var sample = (short)(combinedSignal * 10000);
                
                for (int ch = 0; ch < channels; ch++)
                {
                    var offset = (i * channels + ch) * bytesPerSample;
                    var channelSample = ch == 0 ? sample : (short)(sample * 0.8); // Slight difference between channels
                    var bytes = BitConverter.GetBytes(channelSample);
                    audioData[offset] = bytes[0];
                    audioData[offset + 1] = bytes[1];
                }
            }
            
            return audioData;
        }
        
        /// <summary>
        /// Verify WAV file structure
        /// </summary>
        private async Task VerifyWavFileStructure(string filePath, AudioFormat expectedFormat)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var header = new byte[44];
            await fileStream.ReadAsync(header, 0, 44);
            
            // Basic WAV file validation
            var riffSignature = System.Text.Encoding.ASCII.GetString(header, 0, 4);
            var waveSignature = System.Text.Encoding.ASCII.GetString(header, 8, 4);
            var fmtSignature = System.Text.Encoding.ASCII.GetString(header, 12, 4);
            var dataSignature = System.Text.Encoding.ASCII.GetString(header, 36, 4);
            
            Assert.Equal("RIFF", riffSignature);
            Assert.Equal("WAVE", waveSignature);
            Assert.Equal("fmt ", fmtSignature);
            Assert.Equal("data", dataSignature);
            
            var channels = BitConverter.ToInt16(header, 22);
            var sampleRate = BitConverter.ToInt32(header, 24);
            var bitsPerSample = BitConverter.ToInt16(header, 34);
            
            Assert.Equal(expectedFormat.Channels, channels);
            Assert.Equal(expectedFormat.SampleRate, sampleRate);
            Assert.Equal(expectedFormat.BitsPerSample, bitsPerSample);
        }
        
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testOutputDir))
                {
                    var files = Directory.GetFiles(_testOutputDir, "*.wav");
                    if (files.Length > 0)
                    {
                        Console.WriteLine($"\n📁 Generated audio files in: {_testOutputDir}");
                        Console.WriteLine("🎵 Audio files created:");
                        
                        long totalSize = 0;
                        foreach (var file in files)
                        {
                            var fileInfo = new FileInfo(file);
                            totalSize += fileInfo.Length;
                            Console.WriteLine($"   • {Path.GetFileName(file)} - {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0:F1} KB)");
                        }
                        
                        Console.WriteLine($"📊 Total size: {totalSize:N0} bytes ({totalSize / 1024.0:F1} KB)");
                        Console.WriteLine("🔊 You can play these files to verify audio quality and characteristics.");
                        Console.WriteLine($"💡 Files will be preserved for manual inspection at: {_testOutputDir}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during cleanup: {ex.Message}");
            }
        }
    }
}