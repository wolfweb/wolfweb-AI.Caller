using AI.Caller.Core.Recording;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AI.Caller.Core.Tests.Recording
{
    public class AudioSampleGeneratorTests : IDisposable
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly string _testOutputDir;
        
        public AudioSampleGeneratorTests()
        {
            _mockLogger = new Mock<ILogger>();
            _testOutputDir = Path.Combine(Path.GetTempPath(), "AudioRecordingTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testOutputDir);
        }
        
        [Fact]
        public async Task GenerateAndTestWavFile_ShouldCreateValidWavFile()
        {
            // Arrange
            var options = new AudioEncodingOptions
            {
                Codec = AudioCodec.PCM_WAV,
                SampleRate = 44100,
                Channels = 2,
                BitRate = 128000
            };
            
            var encoder = new FFmpegAudioEncoder(options, _mockLogger.Object);
            var audioFormat = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            var outputPath = Path.Combine(_testOutputDir, "test_stereo_tone.wav");
            
            // Act
            var initResult = await encoder.InitializeAsync(audioFormat, outputPath);
            Assert.True(initResult);
            
            // Generate 3 seconds of stereo sine wave (440Hz left, 880Hz right)
            var duration = TimeSpan.FromSeconds(3);
            var totalSamples = (int)(audioFormat.SampleRate * duration.TotalSeconds);
            var audioData = GenerateStereoSineWave(totalSamples, audioFormat.SampleRate, 440.0, 880.0);
            
            var audioFrame = new AudioFrame(audioData, audioFormat, AudioSource.Mixed);
            var encodeResult = await encoder.EncodeAudioFrameAsync(audioFrame);
            Assert.True(encodeResult);
            
            var finalizeResult = await encoder.FinalizeAsync();
            Assert.True(finalizeResult);
            
            // Assert
            Assert.True(File.Exists(outputPath));
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length > 44); // WAV header is 44 bytes
            
            // Verify WAV file structure
            await VerifyWavFileStructure(outputPath, audioFormat, audioData.Length);
            
            Console.WriteLine($"Generated WAV file: {outputPath} ({fileInfo.Length} bytes)");
        }
        
        [Fact]
        public async Task GenerateAndTestMonoFile_ShouldCreateValidMonoWavFile()
        {
            // Arrange
            var options = new AudioEncodingOptions
            {
                Codec = AudioCodec.PCM_WAV,
                SampleRate = 8000,
                Channels = 1,
                BitRate = 64000
            };
            
            var encoder = new FFmpegAudioEncoder(options, _mockLogger.Object);
            var audioFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var outputPath = Path.Combine(_testOutputDir, "test_mono_tone.wav");
            
            // Act
            var initResult = await encoder.InitializeAsync(audioFormat, outputPath);
            Assert.True(initResult);
            
            // Generate 2 seconds of mono sine wave (1000Hz)
            var duration = TimeSpan.FromSeconds(2);
            var totalSamples = (int)(audioFormat.SampleRate * duration.TotalSeconds);
            var audioData = GenerateMonoSineWave(totalSamples, audioFormat.SampleRate, 1000.0);
            
            var audioFrame = new AudioFrame(audioData, audioFormat, AudioSource.Mixed);
            var encodeResult = await encoder.EncodeAudioFrameAsync(audioFrame);
            Assert.True(encodeResult);
            
            var finalizeResult = await encoder.FinalizeAsync();
            Assert.True(finalizeResult);
            
            // Assert
            Assert.True(File.Exists(outputPath));
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length > 44);
            
            await VerifyWavFileStructure(outputPath, audioFormat, audioData.Length);
            
            Console.WriteLine($"Generated mono WAV file: {outputPath} ({fileInfo.Length} bytes)");
        }
        
        [Fact]
        public async Task GenerateComplexAudioSample_ShouldCreateRealisticAudioFile()
        {
            // Arrange
            var options = new AudioEncodingOptions
            {
                Codec = AudioCodec.PCM_WAV,
                SampleRate = 44100,
                Channels = 2,
                BitRate = 128000
            };
            
            var encoder = new FFmpegAudioEncoder(options, _mockLogger.Object);
            var audioFormat = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            var outputPath = Path.Combine(_testOutputDir, "test_complex_audio.wav");
            
            // Act
            var initResult = await encoder.InitializeAsync(audioFormat, outputPath);
            Assert.True(initResult);
            
            // Generate 5 seconds of complex audio (simulating speech-like patterns)
            var duration = TimeSpan.FromSeconds(5);
            var totalSamples = (int)(audioFormat.SampleRate * duration.TotalSeconds);
            var audioData = GenerateComplexAudioPattern(totalSamples, audioFormat.SampleRate);
            
            var audioFrame = new AudioFrame(audioData, audioFormat, AudioSource.Mixed);
            var encodeResult = await encoder.EncodeAudioFrameAsync(audioFrame);
            Assert.True(encodeResult);
            
            var finalizeResult = await encoder.FinalizeAsync();
            Assert.True(finalizeResult);
            
            // Assert
            Assert.True(File.Exists(outputPath));
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length > 44);
            
            await VerifyWavFileStructure(outputPath, audioFormat, audioData.Length);
            
            Console.WriteLine($"Generated complex audio file: {outputPath} ({fileInfo.Length} bytes)");
        }
        
        [Fact]
        public async Task TestAudioFormatConversion_ShouldConvertBetweenFormats()
        {
            // Arrange
            var converter = new AudioFormatConverter(_mockLogger.Object);
            
            // Generate test audio in 8kHz mono
            var sourceFormat = new AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
            var targetFormat = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            var duration = TimeSpan.FromSeconds(1);
            var totalSamples = (int)(sourceFormat.SampleRate * duration.TotalSeconds);
            var sourceAudioData = GenerateMonoSineWave(totalSamples, sourceFormat.SampleRate, 440.0);
            var sourceFrame = new AudioFrame(sourceAudioData, sourceFormat, AudioSource.RTP_Incoming);
            
            // Act - Convert format
            var convertedFrame = converter.ConvertFormat(sourceFrame, targetFormat);
            
            // Assert
            Assert.NotNull(convertedFrame);
            Assert.Equal(targetFormat.SampleRate, convertedFrame.Format.SampleRate);
            Assert.Equal(targetFormat.Channels, convertedFrame.Format.Channels);
            Assert.True(convertedFrame.Data.Length > sourceFrame.Data.Length); // Should be larger due to upsampling and stereo
            
            // Save both original and converted audio for comparison
            await SaveAudioSample(sourceFrame, Path.Combine(_testOutputDir, "original_8k_mono.wav"));
            await SaveAudioSample(convertedFrame, Path.Combine(_testOutputDir, "converted_44k_stereo.wav"));
            
            Console.WriteLine($"Original: {sourceFrame.Data.Length} bytes, Converted: {convertedFrame.Data.Length} bytes");
        }
        
        [Fact]
        public async Task TestAudioMixing_ShouldMixMultipleAudioSources()
        {
            // Arrange
            var mixer = new AudioMixer(_mockLogger.Object);
            var audioFormat = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            var duration = TimeSpan.FromSeconds(2);
            var totalSamples = (int)(audioFormat.SampleRate * duration.TotalSeconds);
            
            // Generate two different audio sources
            var leftChannelData = GenerateStereoSineWave(totalSamples, audioFormat.SampleRate, 440.0, 0.0); // Only left channel
            var rightChannelData = GenerateStereoSineWave(totalSamples, audioFormat.SampleRate, 0.0, 880.0); // Only right channel
            
            var frame1 = new AudioFrame(leftChannelData, audioFormat, AudioSource.RTP_Incoming);
            var frame2 = new AudioFrame(rightChannelData, audioFormat, AudioSource.RTP_Outgoing);
            
            // Act
            var mixedFrame = mixer.MixFrames(new[] { frame1, frame2 });
            
            // Assert
            Assert.NotNull(mixedFrame);
            Assert.Equal(AudioSource.Mixed, mixedFrame.Source);
            Assert.Equal(audioFormat.SampleRate, mixedFrame.Format.SampleRate);
            Assert.Equal(audioFormat.Channels, mixedFrame.Format.Channels);
            
            // Save individual sources and mixed result
            await SaveAudioSample(frame1, Path.Combine(_testOutputDir, "source1_left_only.wav"));
            await SaveAudioSample(frame2, Path.Combine(_testOutputDir, "source2_right_only.wav"));
            await SaveAudioSample(mixedFrame, Path.Combine(_testOutputDir, "mixed_stereo.wav"));
            
            Console.WriteLine($"Mixed audio from {leftChannelData.Length} + {rightChannelData.Length} = {mixedFrame.Data.Length} bytes");
        }
        
        /// <summary>
        /// Generate mono sine wave audio data
        /// </summary>
        private byte[] GenerateMonoSineWave(int sampleCount, int sampleRate, double frequency)
        {
            var audioData = new byte[sampleCount * 2]; // 16-bit samples
            
            for (int i = 0; i < sampleCount; i++)
            {
                var time = (double)i / sampleRate;
                var amplitude = Math.Sin(2 * Math.PI * frequency * time);
                var sample = (short)(amplitude * 16000); // Scale to 16-bit range
                
                var bytes = BitConverter.GetBytes(sample);
                audioData[i * 2] = bytes[0];
                audioData[i * 2 + 1] = bytes[1];
            }
            
            return audioData;
        }
        
        /// <summary>
        /// Generate stereo sine wave audio data
        /// </summary>
        private byte[] GenerateStereoSineWave(int sampleCount, int sampleRate, double leftFreq, double rightFreq)
        {
            var audioData = new byte[sampleCount * 4]; // 16-bit stereo samples
            
            for (int i = 0; i < sampleCount; i++)
            {
                var time = (double)i / sampleRate;
                
                // Left channel
                var leftAmplitude = leftFreq > 0 ? Math.Sin(2 * Math.PI * leftFreq * time) : 0;
                var leftSample = (short)(leftAmplitude * 16000);
                
                // Right channel
                var rightAmplitude = rightFreq > 0 ? Math.Sin(2 * Math.PI * rightFreq * time) : 0;
                var rightSample = (short)(rightAmplitude * 16000);
                
                // Interleave left and right samples
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
        /// Generate complex audio pattern simulating speech-like characteristics
        /// </summary>
        private byte[] GenerateComplexAudioPattern(int sampleCount, int sampleRate)
        {
            var audioData = new byte[sampleCount * 4]; // 16-bit stereo
            var random = new Random(42); // Fixed seed for reproducible tests
            
            for (int i = 0; i < sampleCount; i++)
            {
                var time = (double)i / sampleRate;
                
                // Create a complex waveform with multiple frequency components
                var fundamental = 200.0 + 100.0 * Math.Sin(2 * Math.PI * 2 * time); // Varying fundamental frequency
                var harmonic1 = Math.Sin(2 * Math.PI * fundamental * time) * 0.8;
                var harmonic2 = Math.Sin(2 * Math.PI * fundamental * 2 * time) * 0.4;
                var harmonic3 = Math.Sin(2 * Math.PI * fundamental * 3 * time) * 0.2;
                
                // Add some noise for realism
                var noise = (random.NextDouble() - 0.5) * 0.1;
                
                // Apply envelope (fade in/out)
                var envelope = 1.0;
                if (time < 0.1) envelope = time / 0.1; // Fade in
                if (time > 4.9) envelope = (5.0 - time) / 0.1; // Fade out
                
                var amplitude = (harmonic1 + harmonic2 + harmonic3 + noise) * envelope;
                
                // Create stereo effect with slight delay
                var leftSample = (short)(amplitude * 12000);
                var rightSample = (short)(amplitude * 12000 * 0.9); // Slightly quieter right channel
                
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
        /// Save audio frame as WAV file
        /// </summary>
        private async Task SaveAudioSample(AudioFrame frame, string filePath)
        {
            var options = new AudioEncodingOptions
            {
                Codec = AudioCodec.PCM_WAV,
                SampleRate = frame.Format.SampleRate,
                Channels = frame.Format.Channels
            };
            
            var encoder = new FFmpegAudioEncoder(options, _mockLogger.Object);
            await encoder.InitializeAsync(frame.Format, filePath);
            await encoder.EncodeAudioFrameAsync(frame);
            await encoder.FinalizeAsync();
            encoder.Dispose();
            
            Console.WriteLine($"Saved audio sample: {filePath}");
        }
        
        /// <summary>
        /// Verify WAV file structure and content
        /// </summary>
        private async Task VerifyWavFileStructure(string filePath, AudioFormat expectedFormat, int expectedDataSize)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var header = new byte[44];
            await fileStream.ReadAsync(header, 0, 44);
            
            // Verify RIFF header
            var riffSignature = System.Text.Encoding.ASCII.GetString(header, 0, 4);
            Assert.Equal("RIFF", riffSignature);
            
            // Verify WAVE format
            var waveSignature = System.Text.Encoding.ASCII.GetString(header, 8, 4);
            Assert.Equal("WAVE", waveSignature);
            
            // Verify fmt chunk
            var fmtSignature = System.Text.Encoding.ASCII.GetString(header, 12, 4);
            Assert.Equal("fmt ", fmtSignature);
            
            // Verify audio format parameters
            var channels = BitConverter.ToInt16(header, 22);
            var sampleRate = BitConverter.ToInt32(header, 24);
            var bitsPerSample = BitConverter.ToInt16(header, 34);
            
            Assert.Equal(expectedFormat.Channels, channels);
            Assert.Equal(expectedFormat.SampleRate, sampleRate);
            Assert.Equal(expectedFormat.BitsPerSample, bitsPerSample);
            
            // Verify data chunk
            var dataSignature = System.Text.Encoding.ASCII.GetString(header, 36, 4);
            Assert.Equal("data", dataSignature);
            
            var dataSize = BitConverter.ToInt32(header, 40);
            Assert.Equal(expectedDataSize, dataSize);
            
            Console.WriteLine($"WAV file verified: {channels}ch, {sampleRate}Hz, {bitsPerSample}bit, {dataSize} bytes data");
        }
        
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testOutputDir))
                {
                    // List generated files for manual inspection
                    var files = Directory.GetFiles(_testOutputDir, "*.wav");
                    if (files.Length > 0)
                    {
                        Console.WriteLine($"\nGenerated audio files in: {_testOutputDir}");
                        foreach (var file in files)
                        {
                            var fileInfo = new FileInfo(file);
                            Console.WriteLine($"  - {Path.GetFileName(file)} ({fileInfo.Length:N0} bytes)");
                        }
                        Console.WriteLine("You can play these files to verify audio quality.");
                    }
                    
                    // Uncomment the next line if you want to auto-cleanup test files
                    // Directory.Delete(_testOutputDir, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }
    }
}