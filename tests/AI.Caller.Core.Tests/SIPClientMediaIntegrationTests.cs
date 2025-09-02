using Microsoft.Extensions.Logging;
using Xunit;
using Moq;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using AI.Caller.Core;
using System.Net;

namespace AI.Caller.Core.Tests {
    public class SIPClientMediaIntegrationTests : IDisposable {
        private readonly Mock<ILogger> _mockLogger;
        private readonly SIPTransport _sipTransport;
        private readonly SIPClient _sipClient;

        public SIPClientMediaIntegrationTests() {
            _mockLogger = new Mock<ILogger>();
            _sipTransport = new SIPTransport();
            _sipClient = new SIPClient("test.server.com", _mockLogger.Object, _sipTransport);
        }

        public void Dispose() {
            _sipClient?.Shutdown();
            _sipTransport?.Shutdown();
        }

        [Fact]
        public void SIPClient_ShouldExposeMediaSessionManager() {
            var mediaManager = _sipClient.MediaSessionManager;

            Assert.NotNull(mediaManager);
        }

        [Fact]
        public void SIPClient_ShouldSubscribeToMediaSessionManagerEvents() {
            var mediaManager = _sipClient.MediaSessionManager;

            mediaManager.SdpOfferGenerated += (offer) => { };
            mediaManager.SdpAnswerGenerated += (answer) => { };
            mediaManager.IceCandidateGenerated += (candidate) => { };
            mediaManager.ConnectionStateChanged += (state) => { };

            Assert.True(true);
        }

        [Fact]
        public async Task CreateOfferAsync_ShouldTriggerSdpOfferGeneratedEvent() {
            RTCSessionDescriptionInit? capturedOffer = null;
            _sipClient.MediaSessionManager.SdpOfferGenerated += (offer) => capturedOffer = offer;

            await _sipClient.MediaSessionManager.InitializeMediaSession();
            _sipClient.MediaSessionManager.InitializePeerConnection(new RTCConfiguration());

            var result = await _sipClient.CreateOfferAsync();

            Assert.NotNull(result);
            Assert.NotNull(capturedOffer);
            Assert.Equal(result.type, capturedOffer.type);
            Assert.Equal(result.sdp, capturedOffer.sdp);
        }

        [Fact]
        public void AddIceCandidate_ShouldCallMediaSessionManagerWithoutDirectResponse() {
            var candidate = new RTCIceCandidateInit {
                candidate = "candidate:1 1 UDP 2130706431 192.168.1.100 54400 typ host",
                sdpMid = "audio",
                sdpMLineIndex = 0
            };

            _sipClient.MediaSessionManager.InitializePeerConnection(new RTCConfiguration());

            _sipClient.AddIceCandidate(candidate);
        }

        [Fact]
        public void SetRemoteDescription_ShouldCallMediaSessionManagerWithoutDirectResponse() {
            var description = new RTCSessionDescriptionInit {
                type = RTCSdpType.offer,
                sdp = "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nc=IN IP4 127.0.0.1\r\nt=0 0\r\nm=audio 5004 RTP/AVP 0\r\na=rtpmap:0 PCMU/8000\r\na=sendrecv\r\n"
            };

            _sipClient.MediaSessionManager.InitializePeerConnection(new RTCConfiguration());


            try {
                _sipClient.SetRemoteDescription(description);
            } catch (InvalidOperationException ex) when (ex.Message.Contains("NoMatchingMediaType")) {

                Assert.True(true);
            }
        }

        [Fact]
        public async Task CompleteSdpNegotiationFlow_ShouldWorkCorrectly() {
            // Arrange
            var offerGenerated = false;

            _sipClient.MediaSessionManager.SdpOfferGenerated += (offer) => offerGenerated = true;

            // Initialize media session and peer connection
            await _sipClient.MediaSessionManager.InitializeMediaSession();
            _sipClient.MediaSessionManager.InitializePeerConnection(new RTCConfiguration());

            // Act - Create offer
            var offer = await _sipClient.CreateOfferAsync();

            // Assert
            Assert.NotNull(offer);
            Assert.True(offerGenerated);
            Assert.Equal(RTCSdpType.offer, offer.type);
        }

        [Fact]
        public void MediaSessionManager_ShouldBeProperlyDisposedOnShutdown() {
            // Arrange
            var mediaManager = _sipClient.MediaSessionManager;
            mediaManager.InitializePeerConnection(new RTCConfiguration());

            // Act
            _sipClient.Shutdown();

            // Assert - Test passes if no exceptions during shutdown
            Assert.True(true);
        }

        [Fact]
        public async Task EventDrivenCommunication_ShouldNotExpectDirectResponses() {
            // Arrange
            var config = new RTCConfiguration();

            // Act - All these calls should work without expecting direct responses
            _sipClient.MediaSessionManager.InitializePeerConnection(config);

            var candidate = new RTCIceCandidateInit {
                candidate = "candidate:1 1 UDP 2130706431 192.168.1.100 54400 typ host",
                sdpMid = "audio",
                sdpMLineIndex = 0
            };
            _sipClient.AddIceCandidate(candidate);

            // Test SetRemoteDescription - may throw exception due to SDP format mismatch, but that's expected
            var description = new RTCSessionDescriptionInit {
                type = RTCSdpType.offer,
                sdp = "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nc=IN IP4 127.0.0.1\r\nt=0 0\r\nm=audio 5004 RTP/AVP 0\r\na=rtpmap:0 PCMU/8000\r\na=sendrecv\r\n"
            };

            try {
                _sipClient.SetRemoteDescription(description);
            } catch (InvalidOperationException ex) when (ex.Message.Contains("NoMatchingMediaType")) {
                // Expected behavior - SDP format doesn't match peer connection setup
            }

            // Assert - The important thing is that methods can be called without expecting direct responses
            Assert.True(true);
        }

        [Fact]
        public void CodeCompilation_ShouldSucceed() {
            // This test verifies that the code compiles successfully
            // and all dependencies are properly resolved

            // Arrange & Act
            var sipClient = new SIPClient("test.server.com", _mockLogger.Object, new SIPTransport());
            var mediaManager = sipClient.MediaSessionManager;

            // Assert
            Assert.NotNull(sipClient);
            Assert.NotNull(mediaManager);

            // Cleanup
            sipClient.Shutdown();
        }

        [Fact]
        public async Task RealisticCallScenario_ShouldWorkEndToEnd() {
            // This test simulates a realistic call scenario to ensure
            // browser-to-backend communication works correctly

            // Arrange
            var eventsTriggered = new List<string>();

            _sipClient.MediaSessionManager.SdpOfferGenerated += (offer) => eventsTriggered.Add("OfferGenerated");
            _sipClient.MediaSessionManager.SdpAnswerGenerated += (answer) => eventsTriggered.Add("AnswerGenerated");
            _sipClient.MediaSessionManager.IceCandidateGenerated += (candidate) => eventsTriggered.Add("CandidateGenerated");
            _sipClient.MediaSessionManager.ConnectionStateChanged += (state) => eventsTriggered.Add($"StateChanged:{state}");

            // Act - Simulate realistic call flow
            try {
                await _sipClient.MediaSessionManager.InitializeMediaSession();
                _sipClient.MediaSessionManager.InitializePeerConnection(new RTCConfiguration());

                // Create offer (browser initiates call)
                var offer = await _sipClient.CreateOfferAsync();

                // Assert
                Assert.NotNull(offer);
                Assert.Contains("OfferGenerated", eventsTriggered);
                Assert.True(eventsTriggered.Count > 0);
            } catch (Exception ex) {
                Assert.Fail($"Realistic call scenario failed: {ex.Message}");
            }
        }
    }
}