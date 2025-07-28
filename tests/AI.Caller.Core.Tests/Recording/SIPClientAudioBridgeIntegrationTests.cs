using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AI.Caller.Core.Recording;
using SIPSorcery.SIP;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Net;

namespace AI.Caller.Core.Tests.Recording
{
    public class SIPClientAudioBridgeIntegrationTests : IDisposable
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly Mock<IAudioBridge> _mockAudioBridge;
        private readonly Mock<SIPTransport> _mockSipTransport;
        private SIPClient? _sipClient;
        
        public SIPClientAudioBridgeIntegrationTests()
        {
            _mockLogger = new Mock<ILogger>();
            _mockAudioBridge = new Mock<IAudioBridge>();
            _mockSipTransport = new Mock<SIPTransport>();
        }
        
        public void Dispose()
        {
            _sipClient?.Shutdown();
        }
        
        [Fact]
        public void SIPClient_WithAudioBridge_ShouldInitializeCorrectly()
        {
            // Act
            _sipClient = new SIPClient("test.com", _mockLogger.Object, _mockSipTransport.Object, _mockAudioBridge.Object);
            
            // Assert
            Assert.NotNull(_sipClient);
            Assert.False(_sipClient.IsCallActive);
        }
        
        [Fact]
        public void SIPClient_WithoutAudioBridge_ShouldInitializeCorrectly()
        {
            // Act
            _sipClient = new SIPClient("test.com", _mockLogger.Object, _mockSipTransport.Object);
            
            // Assert
            Assert.NotNull(_sipClient);
            Assert.False(_sipClient.IsCallActive);
        }
        
        [Fact]
        public void OnRtpPacketReceived_WithAudioBridge_ShouldForwardAudioData()
        {
            // Arrange
            _sipClient = new SIPClient("test.com", _mockLogger.Object, _mockSipTransport.Object, _mockAudioBridge.Object);
            
            var audioData = new byte[] { 1, 2, 3, 4, 5 };
            var rtpPacket = new RTPPacket(audioData);
            var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 5060);
            
            // 使用反射调用私有方法进行测试
            var method = typeof(SIPClient).GetMethod("OnRtpPacketReceived", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Act
            method?.Invoke(_sipClient, new object[] { remoteEndpoint, SDPMediaTypesEnum.audio, rtpPacket });
            
            // Assert
            _mockAudioBridge.Verify(x => x.ForwardAudioData(
                AudioSource.RTP_Incoming, 
                It.Is<byte[]>(data => data.SequenceEqual(audioData)),
                It.IsAny<AI.Caller.Core.Recording.AudioFormat>()
            ), Times.Once);
        }
        
        [Fact]
        public void OnForwardMediaToSIP_WithAudioBridge_ShouldForwardAudioData()
        {
            // Arrange
            _sipClient = new SIPClient("test.com", _mockLogger.Object, _mockSipTransport.Object, _mockAudioBridge.Object);
            
            var audioData = new byte[] { 1, 2, 3, 4, 5 };
            var rtpPacket = new RTPPacket(audioData);
            var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 5060);
            
            // 使用反射调用私有方法进行测试
            var method = typeof(SIPClient).GetMethod("OnForwardMediaToSIP", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Act
            method?.Invoke(_sipClient, new object[] { remoteEndpoint, SDPMediaTypesEnum.audio, rtpPacket });
            
            // Assert
            _mockAudioBridge.Verify(x => x.ForwardAudioData(
                AudioSource.WebRTC_Outgoing, 
                It.Is<byte[]>(data => data.SequenceEqual(audioData)),
                It.IsAny<AI.Caller.Core.Recording.AudioFormat>()
            ), Times.Once);
        }
        
        [Fact]
        public void OnRtpPacketReceived_WithoutAudioBridge_ShouldNotThrow()
        {
            // Arrange
            _sipClient = new SIPClient("test.com", _mockLogger.Object, _mockSipTransport.Object);
            
            var audioData = new byte[] { 1, 2, 3, 4, 5 };
            var rtpPacket = new RTPPacket(audioData);
            var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 5060);
            
            // 使用反射调用私有方法进行测试
            var method = typeof(SIPClient).GetMethod("OnRtpPacketReceived", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Act & Assert - Should not throw
            var exception = Record.Exception(() => 
                method?.Invoke(_sipClient, new object[] { remoteEndpoint, SDPMediaTypesEnum.audio, rtpPacket }));
            
            Assert.Null(exception);
        }
        
        [Fact]
        public void OnRtpPacketReceived_WithEmptyAudioData_ShouldNotForwardToAudioBridge()
        {
            // Arrange
            _sipClient = new SIPClient("test.com", _mockLogger.Object, _mockSipTransport.Object, _mockAudioBridge.Object);
            
            var emptyAudioData = new byte[0];
            var rtpPacket = new RTPPacket(emptyAudioData);
            var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 5060);
            
            // 使用反射调用私有方法进行测试
            var method = typeof(SIPClient).GetMethod("OnRtpPacketReceived", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Act
            method?.Invoke(_sipClient, new object[] { remoteEndpoint, SDPMediaTypesEnum.audio, rtpPacket });
            
            // Assert
            _mockAudioBridge.Verify(x => x.ForwardAudioData(
                It.IsAny<AudioSource>(), 
                It.IsAny<byte[]>(),
                It.IsAny<AI.Caller.Core.Recording.AudioFormat>()
            ), Times.Never);
        }
        
        [Fact]
        public void OnRtpPacketReceived_WithNonAudioMediaType_ShouldNotForwardToAudioBridge()
        {
            // Arrange
            _sipClient = new SIPClient("test.com", _mockLogger.Object, _mockSipTransport.Object, _mockAudioBridge.Object);
            
            var audioData = new byte[] { 1, 2, 3, 4, 5 };
            var rtpPacket = new RTPPacket(audioData);
            var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 5060);
            
            // 使用反射调用私有方法进行测试
            var method = typeof(SIPClient).GetMethod("OnRtpPacketReceived", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Act
            method?.Invoke(_sipClient, new object[] { remoteEndpoint, SDPMediaTypesEnum.video, rtpPacket });
            
            // Assert
            _mockAudioBridge.Verify(x => x.ForwardAudioData(
                It.IsAny<AudioSource>(), 
                It.IsAny<byte[]>(),
                It.IsAny<AI.Caller.Core.Recording.AudioFormat>()
            ), Times.Never);
        }
        
        [Fact]
        public void GetAudioFormat_ShouldReturnCorrectFormat()
        {
            // Arrange
            _sipClient = new SIPClient("test.com", _mockLogger.Object, _mockSipTransport.Object, _mockAudioBridge.Object);
            
            // 使用反射调用私有方法进行测试
            var method = typeof(SIPClient).GetMethod("GetAudioFormat", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Act
            var audioFormat = method?.Invoke(_sipClient, null) as AI.Caller.Core.Recording.AudioFormat;
            
            // Assert
            Assert.NotNull(audioFormat);
            Assert.Equal(8000, audioFormat.SampleRate);
            Assert.Equal(1, audioFormat.Channels);
            Assert.Equal(16, audioFormat.BitsPerSample);
            Assert.Equal(AudioSampleFormat.PCM, audioFormat.SampleFormat);
        }
    }
}