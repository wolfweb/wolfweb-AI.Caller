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
    public class AudioRecorderTests : IDisposable
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly AudioRecorder _audioRecorder;
        
        public AudioRecorderTests()
        {
            _mockLogger = new Mock<ILogger>();
            _audioRecorder = new AudioRecorder(_mockLogger.Object);
        }
        
        [Fact]
        public void Constructor_WithValidLogger_ShouldInitialize()
        {
            // Arrange & Act
            var recorder = new AudioRecorder(_mockLogger.Object);
            
            // Assert
            Assert.False(recorder.IsCapturing);
            Assert.Equal(0, recorder.BufferedFrameCount);
            Assert.Equal(1000, recorder.MaxBufferSize);
        }
        
        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AudioRecorder(null!));
        }
        
        [Fact]
        public async Task StartCaptureAsync_WhenNotCapturing_ShouldStartCapture()
        {
            // Act
            await _audioRecorder.StartCaptureAsync(AudioSource.RTP_Incoming);
            
            // Assert
            Assert.True(_audioRecorder.IsCapturing);
        }
        
        [Fact]
        public async Task StartCaptureAsync_WhenAlreadyCapturing_ShouldNotChangeState()
        {
            // Arrange
            await _audioRecorder.StartCaptureAsync(AudioSource.RTP_Incoming);
            
            // Act
            await _audioRecorder.StartCaptureAsync(AudioSource.RTP_Outgoing);
            
            // Assert
            Assert.True(_audioRecorder.IsCapturing);
        }
        
        [Fact]
        public async Task StopCaptureAsync_WhenCapturing_ShouldStopCapture()
        {
            // Arrange
            await _audioRecorder.StartCaptureAsync(AudioSource.RTP_Incoming);
            
            // Act
            await _audioRecorder.StopCaptureAsync();
            
            // Assert
            Assert.False(_audioRecorder.IsCapturing);
        }
        
        [Fact]
        public async Task StopCaptureAsync_WhenNotCapturing_ShouldNotChangeState()
        {
            // Act
            await _audioRecorder.StopCaptureAsync();
            
            // Assert
            Assert.False(_audioRecorder.IsCapturing);
        }
        
        [Fact]
        public async Task OnRtpAudioReceived_WhenCapturing_ShouldTriggerEvent()
        {
            // Arrange
            await _audioRecorder.StartCaptureAsync(AudioSource.RTP_Incoming);
            
            AudioDataEventArgs? receivedEventArgs = null;
            _audioRecorder.AudioDataReceived += (sender, args) => receivedEventArgs = args;
            
            var rtpPacket = CreateTestRtpPacket();
            var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5060);
            
            // Act
            _audioRecorder.OnRtpAudioReceived(remoteEndPoint, SDPMediaTypesEnum.audio, rtpPacket, AudioSource.RTP_Incoming);
            
            // Assert
            Assert.NotNull(receivedEventArgs);
            Assert.Equal(AudioSource.RTP_Incoming, receivedEventArgs.AudioFrame.Source);
            Assert.Equal(remoteEndPoint, receivedEventArgs.RemoteEndPoint);
            Assert.Equal(rtpPacket.Payload, receivedEventArgs.AudioFrame.Data);
        }
        
        [Fact]
        public void OnRtpAudioReceived_WhenNotCapturing_ShouldNotTriggerEvent()
        {
            // Arrange
            var eventTriggered = false;
            _audioRecorder.AudioDataReceived += (sender, args) => eventTriggered = true;
            
            var rtpPacket = CreateTestRtpPacket();
            var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5060);
            
            // Act
            _audioRecorder.OnRtpAudioReceived(remoteEndPoint, SDPMediaTypesEnum.audio, rtpPacket, AudioSource.RTP_Incoming);
            
            // Assert
            Assert.False(eventTriggered);
        }
        
        [Fact]
        public async Task OnRtpAudioReceived_WithVideoMediaType_ShouldNotTriggerEvent()
        {
            // Arrange
            await _audioRecorder.StartCaptureAsync(AudioSource.RTP_Incoming);
            
            var eventTriggered = false;
            _audioRecorder.AudioDataReceived += (sender, args) => eventTriggered = true;
            
            var rtpPacket = CreateTestRtpPacket();
            var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5060);
            
            // Act
            _audioRecorder.OnRtpAudioReceived(remoteEndPoint, SDPMediaTypesEnum.video, rtpPacket, AudioSource.RTP_Incoming);
            
            // Assert
            Assert.False(eventTriggered);
        }
        
        [Fact]
        public async Task OnWebRtcAudioReceived_WhenCapturing_ShouldTriggerEvent()
        {
            // Arrange
            await _audioRecorder.StartCaptureAsync(AudioSource.WebRTC_Incoming);
            
            AudioDataEventArgs? receivedEventArgs = null;
            _audioRecorder.AudioDataReceived += (sender, args) => receivedEventArgs = args;
            
            var audioData = new byte[] { 1, 2, 3, 4, 5 };
            var audioFormat = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            // Act
            _audioRecorder.OnWebRtcAudioReceived(audioData, audioFormat, AudioSource.WebRTC_Incoming);
            
            // Assert
            Assert.NotNull(receivedEventArgs);
            Assert.Equal(AudioSource.WebRTC_Incoming, receivedEventArgs.AudioFrame.Source);
            Assert.Null(receivedEventArgs.RemoteEndPoint);
            Assert.Equal(audioData, receivedEventArgs.AudioFrame.Data);
            Assert.Equal(audioFormat, receivedEventArgs.AudioFrame.Format);
        }
        
        [Fact]
        public void OnWebRtcAudioReceived_WhenNotCapturing_ShouldNotTriggerEvent()
        {
            // Arrange
            var eventTriggered = false;
            _audioRecorder.AudioDataReceived += (sender, args) => eventTriggered = true;
            
            var audioData = new byte[] { 1, 2, 3, 4, 5 };
            var audioFormat = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            
            // Act
            _audioRecorder.OnWebRtcAudioReceived(audioData, audioFormat, AudioSource.WebRTC_Incoming);
            
            // Assert
            Assert.False(eventTriggered);
        }
        
        [Fact]
        public async Task GetBufferedFrames_WithFramesInBuffer_ShouldReturnFrames()
        {
            // Arrange
            await _audioRecorder.StartCaptureAsync(AudioSource.RTP_Incoming);
            
            var rtpPacket1 = CreateTestRtpPacket(new byte[] { 1, 2, 3 });
            var rtpPacket2 = CreateTestRtpPacket(new byte[] { 4, 5, 6 });
            var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5060);
            
            _audioRecorder.OnRtpAudioReceived(remoteEndPoint, SDPMediaTypesEnum.audio, rtpPacket1, AudioSource.RTP_Incoming);
            _audioRecorder.OnRtpAudioReceived(remoteEndPoint, SDPMediaTypesEnum.audio, rtpPacket2, AudioSource.RTP_Incoming);
            
            // Act
            var frames = _audioRecorder.GetBufferedFrames();
            
            // Assert
            Assert.Equal(2, frames.Count);
            Assert.Equal(new byte[] { 1, 2, 3 }, frames[0].Data);
            Assert.Equal(new byte[] { 4, 5, 6 }, frames[1].Data);
            Assert.Equal(0, _audioRecorder.BufferedFrameCount); // Should be empty after getting frames
        }
        
        [Fact]
        public async Task GetBufferedFrames_WithMaxFramesLimit_ShouldReturnLimitedFrames()
        {
            // Arrange
            await _audioRecorder.StartCaptureAsync(AudioSource.RTP_Incoming);
            
            var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5060);
            for (int i = 0; i < 5; i++)
            {
                var rtpPacket = CreateTestRtpPacket(new byte[] { (byte)i });
                _audioRecorder.OnRtpAudioReceived(remoteEndPoint, SDPMediaTypesEnum.audio, rtpPacket, AudioSource.RTP_Incoming);
            }
            
            // Act
            var frames = _audioRecorder.GetBufferedFrames(3);
            
            // Assert
            Assert.Equal(3, frames.Count);
            Assert.Equal(2, _audioRecorder.BufferedFrameCount); // Should have 2 remaining
        }
        
        [Fact]
        public void ClearBuffer_WithFramesInBuffer_ShouldClearAllFrames()
        {
            // Arrange
            // Add some frames to buffer first
            var audioData = new byte[] { 1, 2, 3 };
            var audioFormat = new AudioFormat(44100, 2, 16, AudioSampleFormat.PCM);
            _audioRecorder.OnWebRtcAudioReceived(audioData, audioFormat, AudioSource.WebRTC_Incoming);
            
            // Act
            _audioRecorder.ClearBuffer();
            
            // Assert
            Assert.Equal(0, _audioRecorder.BufferedFrameCount);
        }
        
        [Fact]
        public async Task BufferOverflow_WhenExceedsMaxSize_ShouldTriggerEvent()
        {
            // Arrange
            await _audioRecorder.StartCaptureAsync(AudioSource.RTP_Incoming);
            _audioRecorder.MaxBufferSize = 5; // Set small buffer size
            
            BufferOverflowEventArgs? overflowEventArgs = null;
            _audioRecorder.BufferOverflow += (sender, args) => overflowEventArgs = args;
            
            var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5060);
            
            // Act - Add more frames than buffer size
            for (int i = 0; i < 10; i++)
            {
                var rtpPacket = CreateTestRtpPacket(new byte[] { (byte)i });
                _audioRecorder.OnRtpAudioReceived(remoteEndPoint, SDPMediaTypesEnum.audio, rtpPacket, AudioSource.RTP_Incoming);
            }
            
            // Assert
            Assert.NotNull(overflowEventArgs);
            Assert.True(overflowEventArgs.RemovedFrameCount > 0);
            Assert.True(_audioRecorder.BufferedFrameCount <= _audioRecorder.MaxBufferSize);
        }
        
        [Fact]
        public void Dispose_WhenCalled_ShouldStopCapturingAndCleanup()
        {
            // Arrange
            _audioRecorder.StartCaptureAsync(AudioSource.RTP_Incoming);
            
            // Act
            _audioRecorder.Dispose();
            
            // Assert
            Assert.False(_audioRecorder.IsCapturing);
            Assert.Equal(0, _audioRecorder.BufferedFrameCount);
        }
        
        [Fact]
        public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
        {
            // Act & Assert
            _audioRecorder.Dispose();
            _audioRecorder.Dispose(); // Should not throw
        }
        
        [Fact]
        public async Task StartCaptureAsync_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _audioRecorder.Dispose();
            
            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _audioRecorder.StartCaptureAsync(AudioSource.RTP_Incoming));
        }
        
        private RTPPacket CreateTestRtpPacket(byte[]? payload = null)
        {
            payload ??= new byte[] { 1, 2, 3, 4, 5 };
            
            // Create RTP packet with header and payload
            var rtpPacketData = new byte[12 + payload.Length]; // 12 bytes for RTP header
            
            // Set RTP header fields
            rtpPacketData[0] = 0x80; // Version 2, no padding, no extension, no CSRC
            rtpPacketData[1] = 0x00; // Payload type 0 (PCMU)
            
            // Sequence number (big endian)
            rtpPacketData[2] = 0x00;
            rtpPacketData[3] = 0x01;
            
            // Timestamp (big endian)
            rtpPacketData[4] = 0x00;
            rtpPacketData[5] = 0x00;
            rtpPacketData[6] = 0x03;
            rtpPacketData[7] = 0xE8; // 1000
            
            // SSRC (big endian)
            rtpPacketData[8] = 0x00;
            rtpPacketData[9] = 0x00;
            rtpPacketData[10] = 0x30;
            rtpPacketData[11] = 0x39; // 12345
            
            // Copy payload
            Array.Copy(payload, 0, rtpPacketData, 12, payload.Length);
            
            return new RTPPacket(rtpPacketData);
        }
        
        public void Dispose()
        {
            _audioRecorder?.Dispose();
        }
    }
}