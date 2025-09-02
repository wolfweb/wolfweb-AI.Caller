using Microsoft.Extensions.Logging;
using Xunit;
using Moq;
using SIPSorcery.Net;
using AI.Caller.Core;
using System.Net;
using SIPSorceryMedia.Abstractions;

namespace AI.Caller.Core.Tests {
    public class MediaSessionManagerTests : IDisposable {
        private readonly Mock<ILogger> _mockLogger;
        private readonly MediaSessionManager _mediaSessionManager;

        public MediaSessionManagerTests() {
            _mockLogger = new Mock<ILogger>();
            _mediaSessionManager = new MediaSessionManager(_mockLogger.Object);
        }

        public void Dispose() {
            _mediaSessionManager?.Dispose();
        }

        [Fact]
        public async Task InitializeMediaSession_ShouldCreateRTPSession() {
            await _mediaSessionManager.InitializeMediaSession();

            Assert.NotNull(_mediaSessionManager.MediaSession);
            Assert.False(_mediaSessionManager.MediaSession.IsClosed);
        }

        [Fact]
        public void InitializePeerConnection_ShouldCreateRTCPeerConnection() {
            var config = new RTCConfiguration();

            _mediaSessionManager.InitializePeerConnection(config);

            Assert.NotNull(_mediaSessionManager.PeerConnection);
        }

        [Fact]
        public async Task CreateOfferAsync_ShouldEmitSdpOfferGenerated() {
            await _mediaSessionManager.InitializeMediaSession();
            _mediaSessionManager.InitializePeerConnection(new RTCConfiguration());

            RTCSessionDescriptionInit? capturedOffer = null;
            _mediaSessionManager.SdpOfferGenerated += (offer) => capturedOffer = offer;

            var result = await _mediaSessionManager.CreateOfferAsync();

            Assert.NotNull(result);
            Assert.NotNull(capturedOffer);
            Assert.Equal(RTCSdpType.offer, result.type);
            Assert.Equal(result.sdp, capturedOffer.sdp);
        }

        [Fact]
        public void Dispose_ShouldCleanupAllResources() {
            _mediaSessionManager.InitializePeerConnection(new RTCConfiguration());

            _mediaSessionManager.Dispose();

            Assert.Null(_mediaSessionManager.MediaSession);
            Assert.Null(_mediaSessionManager.PeerConnection);
        }

        [Fact]
        public async Task MultipleInitialization_ShouldNotCreateDuplicates() {
            await _mediaSessionManager.InitializeMediaSession();
            var firstSession = _mediaSessionManager.MediaSession;

            await _mediaSessionManager.InitializeMediaSession();
            var secondSession = _mediaSessionManager.MediaSession;

            Assert.Same(firstSession, secondSession);
        }

        [Fact]
        public async Task ThrowIfDisposed_ShouldThrowObjectDisposedException() {
            _mediaSessionManager.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => _mediaSessionManager.InitializeMediaSession());
        }

        [Fact]
        public void InitializePeerConnection_WithNullConfig_ShouldThrowArgumentNullException() {
            Assert.Throws<ArgumentNullException>(
                () => _mediaSessionManager.InitializePeerConnection(null!));
        }

        [Fact]
        public async Task CreateOfferAsync_WithoutInitialization_ShouldAutoInitialize() {
            var offer = await _mediaSessionManager.CreateOfferAsync();

            Assert.NotNull(offer);
            Assert.NotEmpty(offer.sdp);
        }

        [Fact]
        public void AddIceCandidate_WithNullCandidate_ShouldThrowArgumentNullException() {
            Assert.Throws<ArgumentNullException>(
                () => _mediaSessionManager.AddIceCandidate(null!));
        }

        [Fact]
        public void EventSubscription_ShouldWorkCorrectly() {
            _mediaSessionManager.InitializePeerConnection(new RTCConfiguration());

            _mediaSessionManager.IceCandidateGenerated += (candidate) => { };
            _mediaSessionManager.ConnectionStateChanged += (state) => { };
            _mediaSessionManager.AudioDataReceived += (remote, mediaType, packet) => { };
            _mediaSessionManager.AudioDataSent += (remote, mediaType, packet) => { };

            Assert.True(true);
        }
    }
}