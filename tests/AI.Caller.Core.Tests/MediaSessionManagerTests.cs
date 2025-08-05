using Microsoft.Extensions.Logging;
using Xunit;
using Moq;
using SIPSorcery.Net;
using AI.Caller.Core;
using System.Net;
using SIPSorceryMedia.Abstractions;

namespace AI.Caller.Core.Tests
{
    public class MediaSessionManagerTests : IDisposable
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly MediaSessionManager _mediaSessionManager;

        public MediaSessionManagerTests()
        {
            _mockLogger = new Mock<ILogger>();
            _mediaSessionManager = new MediaSessionManager(_mockLogger.Object);
        }

        public void Dispose()
        {
            _mediaSessionManager?.Dispose();
        }

        [Fact]
        public async Task InitializeMediaSession_ShouldCreateRTPSession()
        {
            // Act
            await _mediaSessionManager.InitializeMediaSession();

            // Assert
            Assert.NotNull(_mediaSessionManager.MediaSession);
            Assert.False(_mediaSessionManager.MediaSession.IsClosed);
        }

        [Fact]
        public void InitializePeerConnection_ShouldCreateRTCPeerConnection()
        {
            // Arrange
            var config = new RTCConfiguration();

            // Act
            _mediaSessionManager.InitializePeerConnection(config);

            // Assert
            Assert.NotNull(_mediaSessionManager.PeerConnection);
        }

        [Fact]
        public async Task CreateOfferAsync_ShouldEmitSdpOfferGenerated()
        {
            // Arrange
            await _mediaSessionManager.InitializeMediaSession();
            _mediaSessionManager.InitializePeerConnection(new RTCConfiguration());
            
            RTCSessionDescriptionInit? capturedOffer = null;
            _mediaSessionManager.SdpOfferGenerated += (offer) => capturedOffer = offer;

            // Act
            var result = await _mediaSessionManager.CreateOfferAsync();

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(capturedOffer);
            Assert.Equal(RTCSdpType.offer, result.type);
            Assert.Equal(result.sdp, capturedOffer.sdp);
        }

        [Fact]
        public void Dispose_ShouldCleanupAllResources()
        {
            // Arrange
            _mediaSessionManager.InitializePeerConnection(new RTCConfiguration());

            // Act
            _mediaSessionManager.Dispose();

            // Assert
            Assert.Null(_mediaSessionManager.MediaSession);
            Assert.Null(_mediaSessionManager.PeerConnection);
        }

        [Fact]
        public async Task MultipleInitialization_ShouldNotCreateDuplicates()
        {
            // Act
            await _mediaSessionManager.InitializeMediaSession();
            var firstSession = _mediaSessionManager.MediaSession;
            
            await _mediaSessionManager.InitializeMediaSession();
            var secondSession = _mediaSessionManager.MediaSession;

            // Assert
            Assert.Same(firstSession, secondSession);
        }

        [Fact]
        public async Task ThrowIfDisposed_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _mediaSessionManager.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => _mediaSessionManager.InitializeMediaSession());
        }

        [Fact]
        public void InitializePeerConnection_WithNullConfig_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => _mediaSessionManager.InitializePeerConnection(null!));
        }

        [Fact]
        public async Task CreateOfferAsync_WithoutInitialization_ShouldAutoInitialize()
        {
            // Act - Should not throw exception, should auto-initialize
            var offer = await _mediaSessionManager.CreateOfferAsync();
            
            // Assert
            Assert.NotNull(offer);
            Assert.NotEmpty(offer.sdp);
        }

        [Fact]
        public void AddIceCandidate_WithNullCandidate_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => _mediaSessionManager.AddIceCandidate(null!));
        }

        [Fact]
        public void EventSubscription_ShouldWorkCorrectly()
        {
            // Arrange
            _mediaSessionManager.InitializePeerConnection(new RTCConfiguration());
            
            // Act - Subscribe to events (should not throw exceptions)
            _mediaSessionManager.IceCandidateGenerated += (candidate) => { };
            _mediaSessionManager.ConnectionStateChanged += (state) => { };
            _mediaSessionManager.AudioDataReceived += (remote, mediaType, packet) => { };
            _mediaSessionManager.AudioDataSent += (remote, mediaType, packet) => { };

            // Assert - Events can be subscribed without errors
            Assert.True(true); // Test passes if no exceptions are thrown during event subscription
        }
    }
}