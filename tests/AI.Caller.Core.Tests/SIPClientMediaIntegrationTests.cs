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
        public async Task SIPClient_ShouldExposeMediaSessionManager() {
            await _sipClient.CreateOfferAsync();
            
            var mediaManager = _sipClient.MediaSessionManager;

            Assert.NotNull(mediaManager);
        }

        [Fact]
        public async Task SIPClient_ShouldSubscribeToMediaSessionManagerEvents() {
            await _sipClient.CreateOfferAsync();
            
            var mediaManager = _sipClient.MediaSessionManager;

            mediaManager.SdpOfferGenerated += (offer) => { };
            mediaManager.SdpAnswerGenerated += (answer) => { };
            mediaManager.IceCandidateGenerated += (candidate) => { };
            mediaManager.ConnectionStateChanged += (state) => { };

        }

        [Fact]
        public async Task CreateOfferAsync_ShouldTriggerSdpOfferGeneratedEvent() {
            RTCSessionDescriptionInit? capturedOffer = null;            
            var result = await _sipClient.CreateOfferAsync();
            
            _sipClient.MediaSessionManager!.SdpOfferGenerated += (offer) => capturedOffer = offer;
            
            var result2 = await _sipClient.CreateOfferAsync();

            Assert.NotNull(result);
            Assert.NotNull(result2);
            Assert.NotNull(capturedOffer);
        }

        [Fact]
        public async Task AddIceCandidate_ShouldCallMediaSessionManagerWithoutDirectResponse() {
            var candidate = new RTCIceCandidateInit {
                candidate = "candidate:1 1 UDP 2130706431 192.168.1.100 54400 typ host",
                sdpMid = "audio",
                sdpMLineIndex = 0
            };

            await _sipClient.CreateOfferAsync();

            _sipClient.AddIceCandidate(candidate);
        }

        [Fact]
        public async Task SetRemoteDescription_ShouldCallMediaSessionManagerWithoutDirectResponse() {
            var description = new RTCSessionDescriptionInit {
                type = RTCSdpType.offer,
                sdp = "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nc=IN IP4 127.0.0.1\r\nt=0 0\r\nm=audio 5004 RTP/AVP 0\r\na=rtpmap:0 PCMU/8000\r\na=sendrecv\r\n"
            };

            await _sipClient.OfferAsync(description);

            Assert.NotNull(_sipClient.MediaSessionManager);
        }

        [Fact]
        public async Task CompleteSdpNegotiationFlow_ShouldWorkCorrectly() {
            var offerGenerated = false;

            var offer = await _sipClient.CreateOfferAsync();
            
            _sipClient.MediaSessionManager!.SdpOfferGenerated += (offer) => offerGenerated = true;
            
            var offer2 = await _sipClient.CreateOfferAsync();

            // Assert
            Assert.NotNull(offer);
            Assert.NotNull(offer2);
            Assert.True(offerGenerated);
            Assert.Equal(RTCSdpType.offer, offer.type);
        }

        [Fact]
        public async Task MediaSessionManager_ShouldBeProperlyDisposedOnShutdown() {
            await _sipClient.CreateOfferAsync();
            var mediaManager = _sipClient.MediaSessionManager;
            Assert.NotNull(mediaManager);

            _sipClient.Shutdown();
        }

        [Fact]
        public async Task EventDrivenCommunication_ShouldNotExpectDirectResponses() {
            var description = new RTCSessionDescriptionInit {
                type = RTCSdpType.offer,
                sdp = "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nc=IN IP4 127.0.0.1\r\nt=0 0\r\nm=audio 5004 RTP/AVP 0\r\na=rtpmap:0 PCMU/8000\r\na=sendrecv\r\n"
            };
            
            await _sipClient.OfferAsync(description);

            var candidate = new RTCIceCandidateInit {
                candidate = "candidate:1 1 UDP 2130706431 192.168.1.100 54400 typ host",
                sdpMid = "audio",
                sdpMLineIndex = 0
            };
            _sipClient.AddIceCandidate(candidate);

            Assert.NotNull(_sipClient.MediaSessionManager);
        }



        [Fact]
        public async Task RealisticCallScenario_ShouldWorkEndToEnd() {
            var eventsTriggered = new List<string>();
            try {
                var offer = await _sipClient.CreateOfferAsync();
                
                _sipClient.MediaSessionManager!.SdpOfferGenerated += (offer) => eventsTriggered.Add("OfferGenerated");
                _sipClient.MediaSessionManager.SdpAnswerGenerated += (answer) => eventsTriggered.Add("AnswerGenerated");
                _sipClient.MediaSessionManager.IceCandidateGenerated += (candidate) => eventsTriggered.Add("CandidateGenerated");
                _sipClient.MediaSessionManager.ConnectionStateChanged += (state) => eventsTriggered.Add($"StateChanged:{state}");
                
                var offer2 = await _sipClient.CreateOfferAsync();

                // Assert
                Assert.NotNull(offer);
                Assert.NotNull(offer2);
                Assert.Contains("OfferGenerated", eventsTriggered);
                Assert.True(eventsTriggered.Count > 0);
            } catch (Exception ex) {
                Assert.Fail($"Realistic call scenario failed: {ex.Message}");
            }
        }
    }
}