using AI.Caller.Core.Recording;
using Microsoft.Extensions.Logging;
using Moq;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using System.Net;
using Xunit;
using AudioFormat = AI.Caller.Core.Recording.AudioFormat;

namespace AI.Caller.Core.Tests.Recording
{
    public class SIPClientRecordingIntegrationTests : IDisposable
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly Mock<SIPTransport> _mockSipTransport;
        private readonly SIPClientOptions _sipClientOptions;
        private readonly string _testOutputDir;
        private readonly List<SIPClient> _sipClients;
        
        public SIPClientRecordingIntegrationTests()
        {
            _mockLogger = new Mock<ILogger>();
            _mockSipTransport = new Mock<SIPTransport>();
            _sipClientOptions = new SIPClientOptions
            {
                SIPUsername = "testuser",
                SIPPassword = "testpass",
                SIPServer = "test.server.com",
                SIPFromName = "Test User"
            };
            
            _testOutputDir = Path.Combine(Path.GetTempPath(), "SIPClientRecordingTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testOutputDir);
            
            _sipClients = new List<SIPClient>();
        }
        
        [Fact]
        public void SetRecordingManager_ShouldConfigureRecordingCorrectly()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            mockRecordingManager.Setup(x => x.CurrentStatus).Returns(() => new RecordingStatus());
            var recordingOptions = CreateTestRecordingOptions();
            
            // Act
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions, autoRecording: true);
            
            // Assert
            Assert.True(sipClient.IsRecordingEnabled);
            Assert.False(sipClient.IsRecording); // Not recording until call starts
            Assert.NotNull(sipClient.RecordingStatus);
        }
        
        [Fact]
        public void SetRecordingManager_WithNullManager_ShouldThrowArgumentNullException()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var recordingOptions = CreateTestRecordingOptions();
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                sipClient.SetRecordingManager(null!, recordingOptions));
        }
        
        [Fact]
        public void SetRecordingManager_WithNullOptions_ShouldThrowArgumentNullException()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                sipClient.SetRecordingManager(mockRecordingManager.Object, null!));
        }
        
        [Fact]
        public async Task StartRecordingAsync_WithoutRecordingManager_ShouldReturnFalse()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            
            // Act
            var result = await sipClient.StartRecordingAsync();
            
            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public async Task StartRecordingAsync_WithoutActiveCall_ShouldReturnFalse()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            var recordingOptions = CreateTestRecordingOptions();
            
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions);
            
            // Act
            var result = await sipClient.StartRecordingAsync();
            
            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public async Task StartRecordingAsync_WithActiveCall_ShouldStartRecording()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            var recordingOptions = CreateTestRecordingOptions();
            
            // Setup recording manager to return success
            mockRecordingManager.Setup(m => m.StartRecordingAsync(It.IsAny<RecordingOptions>()))
                              .ReturnsAsync(true);
            
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions);
            
            // Simulate active call
            SimulateActiveCall(sipClient);
            
            // Act
            var result = await sipClient.StartRecordingAsync();
            
            // Assert
            Assert.True(result);
            mockRecordingManager.Verify(m => m.StartRecordingAsync(It.IsAny<RecordingOptions>()), Times.Once);
        }
        
        [Fact]
        public async Task StopRecordingAsync_WithoutRecordingManager_ShouldReturnNull()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            
            // Act
            var result = await sipClient.StopRecordingAsync();
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public async Task StopRecordingAsync_WhenNotRecording_ShouldReturnNull()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            var recordingOptions = CreateTestRecordingOptions();
            
            mockRecordingManager.Setup(m => m.IsRecording).Returns(false);
            
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions);
            
            // Act
            var result = await sipClient.StopRecordingAsync();
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public async Task StopRecordingAsync_WhenRecording_ShouldStopAndReturnFilePath()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            var recordingOptions = CreateTestRecordingOptions();
            var expectedFilePath = Path.Combine(_testOutputDir, "test_recording.wav");
            
            mockRecordingManager.Setup(m => m.IsRecording).Returns(true);
            mockRecordingManager.Setup(m => m.StopRecordingAsync()).ReturnsAsync(expectedFilePath);
            
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions);
            
            // Act
            var result = await sipClient.StopRecordingAsync();
            
            // Assert
            Assert.Equal(expectedFilePath, result);
            mockRecordingManager.Verify(m => m.StopRecordingAsync(), Times.Once);
        }
        
        [Fact]
        public async Task PauseRecordingAsync_ShouldCallRecordingManager()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            var recordingOptions = CreateTestRecordingOptions();
            
            mockRecordingManager.Setup(m => m.PauseRecordingAsync()).ReturnsAsync(true);
            
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions);
            
            // Act
            var result = await sipClient.PauseRecordingAsync();
            
            // Assert
            Assert.True(result);
            mockRecordingManager.Verify(m => m.PauseRecordingAsync(), Times.Once);
        }
        
        [Fact]
        public async Task ResumeRecordingAsync_ShouldCallRecordingManager()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            var recordingOptions = CreateTestRecordingOptions();
            
            mockRecordingManager.Setup(m => m.ResumeRecordingAsync()).ReturnsAsync(true);
            
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions);
            
            // Act
            var result = await sipClient.ResumeRecordingAsync();
            
            // Assert
            Assert.True(result);
            mockRecordingManager.Verify(m => m.ResumeRecordingAsync(), Times.Once);
        }
        
        [Fact]
        public async Task CancelRecordingAsync_ShouldCallRecordingManager()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            var recordingOptions = CreateTestRecordingOptions();
            
            mockRecordingManager.Setup(m => m.CancelRecordingAsync()).ReturnsAsync(true);
            
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions);
            
            // Act
            var result = await sipClient.CancelRecordingAsync();
            
            // Assert
            Assert.True(result);
            mockRecordingManager.Verify(m => m.CancelRecordingAsync(), Times.Once);
        }
        
        [Fact]
        public void RecordingStatusChanged_ShouldTriggerEvents()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            var recordingOptions = CreateTestRecordingOptions();
            
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions);
            
            string? recordingStartedFile = null;
            string? recordingStoppedFile = null;
            RecordingErrorEventArgs? recordingError = null;
            
            sipClient.RecordingStarted += (client, filePath) => recordingStartedFile = filePath;
            sipClient.RecordingStopped += (client, filePath) => recordingStoppedFile = filePath;
            sipClient.RecordingError += (client, error) => recordingError = error;
            
            // Act - Simulate recording started
            var startedStatus = new RecordingStatus();
            startedStatus.UpdateState(RecordingState.Recording, "Recording started");
            startedStatus.CurrentFilePath = "test_recording.wav";
            
            var startedEventArgs = new RecordingStatusEventArgs(startedStatus);
            mockRecordingManager.Raise(m => m.StatusChanged += null, mockRecordingManager.Object, startedEventArgs);
            
            // Act - Simulate recording completed
            var completedStatus = new RecordingStatus();
            completedStatus.UpdateState(RecordingState.Completed, "Recording completed");
            completedStatus.CurrentFilePath = "test_recording.wav";
            
            var completedEventArgs = new RecordingStatusEventArgs(completedStatus);
            mockRecordingManager.Raise(m => m.StatusChanged += null, mockRecordingManager.Object, completedEventArgs);
            
            // Act - Simulate recording error
            var errorStatus = new RecordingStatus();
            errorStatus.SetError(RecordingErrorCode.EncodingFailed, "Encoding failed");
            
            var errorEventArgs = new RecordingStatusEventArgs(errorStatus);
            mockRecordingManager.Raise(m => m.StatusChanged += null, mockRecordingManager.Object, errorEventArgs);
            
            // Assert
            Assert.Equal("test_recording.wav", recordingStartedFile);
            Assert.Equal("test_recording.wav", recordingStoppedFile);
            Assert.NotNull(recordingError);
            Assert.Equal(RecordingErrorCode.Unknown, recordingError.ErrorCode); // Mapped to Unknown in SIPClient
        }
        
        [Fact]
        public void RecordingErrorOccurred_ShouldTriggerErrorEvent()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            var recordingOptions = CreateTestRecordingOptions();
            
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions);
            
            RecordingErrorEventArgs? capturedError = null;
            sipClient.RecordingError += (client, error) => capturedError = error;
            
            // Act
            var errorEventArgs = new RecordingErrorEventArgs(RecordingErrorCode.EncodingFailed, "Test error message");
            mockRecordingManager.Raise(m => m.ErrorOccurred += null, mockRecordingManager.Object, errorEventArgs);
            
            // Assert
            Assert.NotNull(capturedError);
            Assert.Equal(RecordingErrorCode.EncodingFailed, capturedError.ErrorCode);
            Assert.Equal("Test error message", capturedError.ErrorMessage);
        }
        
        [Fact]
        public void AutoRecording_ShouldStartOnCallAnswer()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            var recordingOptions = CreateTestRecordingOptions();
            
            mockRecordingManager.Setup(m => m.StartRecordingAsync(It.IsAny<RecordingOptions>()))
                              .ReturnsAsync(true);
            
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions, autoRecording: true);
            
            // Simulate active call
            SimulateActiveCall(sipClient);
            
            // Act - Trigger CallAnswer event
            sipClient.GetType()
                    .GetEvent("CallAnswer")?
                    .GetRaiseMethod(true)?
                    .Invoke(sipClient, new object[] { sipClient });
            
            // Wait a bit for async operation
            Thread.Sleep(100);
            
            // Assert
            mockRecordingManager.Verify(m => m.StartRecordingAsync(It.IsAny<RecordingOptions>()), Times.Once);
        }
        
        [Fact]
        public void AutoRecording_ShouldStopOnCallEnd()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            var mockRecordingManager = new Mock<IAudioRecordingManager>();
            var recordingOptions = CreateTestRecordingOptions();
            
            mockRecordingManager.Setup(m => m.IsRecording).Returns(true);
            mockRecordingManager.Setup(m => m.StopRecordingAsync()).ReturnsAsync("test_file.wav");
            
            sipClient.SetRecordingManager(mockRecordingManager.Object, recordingOptions, autoRecording: true);
            
            // Act - Trigger CallEnded event
            sipClient.GetType()
                    .GetEvent("CallEnded")?
                    .GetRaiseMethod(true)?
                    .Invoke(sipClient, new object[] { sipClient });
            
            // Wait a bit for async operation
            Thread.Sleep(100);
            
            // Assert
            mockRecordingManager.Verify(m => m.StopRecordingAsync(), Times.Once);
        }
        
        [Fact]
        public async Task CompleteRecordingWorkflow_ShouldWorkEndToEnd()
        {
            // Arrange
            var sipClient = CreateSIPClient();
            
            // Create real recording components
            var audioRecorder = new AudioRecorder(_mockLogger.Object);
            var audioMixer = new AudioMixer(_mockLogger.Object);
            var encodingOptions = new AudioEncodingOptions
            {
                Codec = AudioCodec.PCM_WAV,
                SampleRate = 8000,
                Channels = 1,
                BitRate = 64000,
                Quality = AudioQuality.Standard
            };
            var audioEncoder = new FFmpegAudioEncoder(encodingOptions, _mockLogger.Object);
            var storageOptions = new RecordingStorageOptions
            {
                OutputDirectory = _testOutputDir,
                MaxFileSize = 100 * 1024 * 1024, // 100MB
                FileNameTemplate = "siptest_{timestamp}",
                AutoCleanup = new AutoCleanupOptions
                {
                    Enabled = false,
                }
            };
            var fileManager = new RecordingFileManager(storageOptions, _mockLogger.Object);
            var formatConverter = new AudioFormatConverter(_mockLogger.Object);
            
            var recordingManager = new AudioRecordingManager(
                audioRecorder, audioMixer, audioEncoder, fileManager, formatConverter, _mockLogger.Object);
            
            var recordingOptions = new RecordingOptions
            {
                Codec = AudioCodec.PCM_WAV,
                SampleRate = 8000,
                Channels = 1,
                OutputDirectory = _testOutputDir,
                FileNameTemplate = "siptest_{timestamp}",
                MaxDuration = TimeSpan.FromSeconds(10)
            };
            
            sipClient.SetRecordingManager(recordingManager, recordingOptions, autoRecording: false);
            
            // Simulate active call
            SimulateActiveCall(sipClient);
            
            // Act
            var startResult = await sipClient.StartRecordingAsync();
            Assert.True(startResult);
            Assert.True(sipClient.IsRecording);
            
            // Simulate some audio data
            await SimulateAudioData(sipClient, TimeSpan.FromSeconds(2));
            
            var filePath = await sipClient.StopRecordingAsync();
            
            // Assert
            Assert.NotNull(filePath);
            Assert.True(File.Exists(filePath));
            Assert.False(sipClient.IsRecording);
            
            var fileInfo = new FileInfo(filePath);
            Assert.True(fileInfo.Length > 0);
            
            // Cleanup
            recordingManager.Dispose();
        }
        
        private SIPClient CreateSIPClient()
        {
            var sipClient = new SIPClient(_mockLogger.Object, _sipClientOptions, _mockSipTransport.Object);
            _sipClients.Add(sipClient);
            return sipClient;
        }
        
        private RecordingOptions CreateTestRecordingOptions()
        {
            return new RecordingOptions
            {
                Codec = AudioCodec.PCM_WAV,
                SampleRate = 8000,
                Channels = 1,
                BitRate = 64000,
                OutputDirectory = _testOutputDir,
                FileNameTemplate = "test_{timestamp}",
                MaxDuration = TimeSpan.FromMinutes(5),
                RecordBothParties = true
            };
        }
        
        private void SimulateActiveCall(SIPClient sipClient)
        {
            // Use reflection to set IsCallActive to true
            var userAgentField = sipClient.GetType().GetField("m_userAgent", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (userAgentField != null)
            {
                // 使用反射设置私有字段来模拟活动通话状态
                // 由于SIPUserAgent是具体类，我们需要创建一个实例
                var sipTransport = new Mock<SIPTransport>().Object;
                var userAgent = new SIPUserAgent(sipTransport, null);
                
                // 使用反射设置IsCallActive属性
                var isCallActiveProperty = typeof(SIPUserAgent).GetProperty("IsCallActive");
                if (isCallActiveProperty != null && isCallActiveProperty.CanWrite)
                {
                    isCallActiveProperty.SetValue(userAgent, true);
                }
                
                userAgentField.SetValue(sipClient, userAgent);
            }
        }
        
        private async Task SimulateAudioData(SIPClient sipClient, TimeSpan duration)
        {
            var endTime = DateTime.UtcNow.Add(duration);
            var frameInterval = TimeSpan.FromMilliseconds(20); // 20ms frames
            
            while (DateTime.UtcNow < endTime)
            {
                // Generate test audio data
                var audioData = GenerateTestAudioData(160); // 20ms at 8kHz = 160 samples
                var rtpPacket = new RTPPacket(audioData);
                var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 5060);
                
                // Simulate RTP packet received
                try
                {
                    var forwardMethod = sipClient.GetType().GetMethod("ForwardAudioToRecording", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    forwardMethod?.Invoke(sipClient, new object[] 
                    { 
                        remoteEndpoint, 
                        SDPMediaTypesEnum.audio, 
                        rtpPacket, 
                        AudioSource.RTP_Incoming 
                    });
                }
                catch (Exception ex)
                {
                    // Ignore reflection errors in test
                    Console.WriteLine($"Simulation error: {ex.Message}");
                }
                
                await Task.Delay(frameInterval);
            }
        }
        
        private byte[] GenerateTestAudioData(int sampleCount)
        {
            var audioData = new byte[sampleCount * 2]; // 16-bit samples
            var random = new Random();
            
            for (int i = 0; i < sampleCount; i++)
            {
                // Generate simple sine wave
                var time = i / 8000.0; // 8kHz sample rate
                var amplitude = Math.Sin(2 * Math.PI * 440 * time); // 440Hz tone
                var sample = (short)(amplitude * 8000);
                
                var bytes = BitConverter.GetBytes(sample);
                audioData[i * 2] = bytes[0];
                audioData[i * 2 + 1] = bytes[1];
            }
            
            return audioData;
        }
        
        public void Dispose()
        {
            try
            {
                foreach (var sipClient in _sipClients)
                {
                    try
                    {
                        sipClient.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error shutting down SIPClient: {ex.Message}");
                    }
                }
                
                if (Directory.Exists(_testOutputDir))
                {
                    var files = Directory.GetFiles(_testOutputDir, "*.*");
                    if (files.Length > 0)
                    {
                        Console.WriteLine($"\n📁 Generated test files in: {_testOutputDir}");
                        foreach (var file in files)
                        {
                            var fileInfo = new FileInfo(file);
                            Console.WriteLine($"   • {Path.GetFileName(file)} - {fileInfo.Length:N0} bytes");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during test cleanup: {ex.Message}");
            }
        }
    }
}