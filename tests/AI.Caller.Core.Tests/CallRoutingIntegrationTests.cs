using Microsoft.Extensions.Logging;
using Xunit;
using Moq;
using AI.Caller.Core;
using AI.Caller.Phone.CallRouting.Services;
using AI.Caller.Phone.CallRouting.Interfaces;
using AI.Caller.Phone.CallRouting.Models;
using SIPSorcery.SIP;

namespace AI.Caller.Core.Tests {
    public class CallRoutingIntegrationTests : IDisposable {
        private readonly Mock<ILogger<CallTypeIdentifier>> _mockCallTypeLogger;
        private readonly CallTypeIdentifier _callTypeIdentifier;

        public CallRoutingIntegrationTests() {
            _mockCallTypeLogger = new Mock<ILogger<CallTypeIdentifier>>();
            _callTypeIdentifier = new CallTypeIdentifier(_mockCallTypeLogger.Object);
        }

        [Fact]
        public void MediaSessionManager_ShouldIntegrateWithCallRouting() {
            var logger = new Mock<ILogger>();
            var mediaManager = new MediaSessionManager(logger.Object);

            Assert.NotNull(mediaManager);
            Assert.Null(mediaManager.MediaSession);
            Assert.Null(mediaManager.PeerConnection);

            var eventTriggered = false;
            mediaManager.SdpOfferGenerated += (offer) => eventTriggered = true;

            mediaManager.Dispose();
            Assert.False(eventTriggered);
        }

        [Fact]
        public void CallTypeIdentifier_ShouldTrackOutboundCalls() {
            var callId = "outbound-call-123";
            var fromTag = "from-tag-456";
            var sipUsername = "caller@example.com";
            var destination = "1234567890";

            _callTypeIdentifier.RegisterOutboundCallWithSipTags(callId, fromTag, sipUsername, destination);
            var callInfo = _callTypeIdentifier.GetOutboundCallInfo(callId);

            Assert.NotNull(callInfo);
            Assert.Equal(callId, callInfo.CallId);
            Assert.Equal(fromTag, callInfo.FromTag);
            Assert.Equal(sipUsername, callInfo.SipUsername);
            Assert.Equal(destination, callInfo.Destination);
            Assert.Equal(CallStatus.Initiated, callInfo.Status);
        }

        [Fact]
        public void CallTypeIdentifier_ShouldUpdateCallStatus() {
            var callId = "status-update-call-123";
            var sipUsername = "caller@example.com";

            _callTypeIdentifier.RegisterOutboundCallWithSipTags(callId, "tag", sipUsername, "dest");

            _callTypeIdentifier.UpdateOutboundCallStatus(callId, CallStatus.Answered);
            var callInfo = _callTypeIdentifier.GetOutboundCallInfo(callId);

            Assert.NotNull(callInfo);
            Assert.Equal(CallStatus.Answered, callInfo.Status);
        }

        [Fact]
        public void CallTypeIdentifier_ShouldReturnActiveCallsOnly() {
            _callTypeIdentifier.RegisterOutboundCallWithSipTags("active1", "tag1", "user1", "dest1");
            _callTypeIdentifier.RegisterOutboundCallWithSipTags("active2", "tag2", "user2", "dest2");
            _callTypeIdentifier.RegisterOutboundCallWithSipTags("ended", "tag3", "user3", "dest3");

            _callTypeIdentifier.UpdateOutboundCallStatus("ended", CallStatus.Ended);

            var activeCalls = _callTypeIdentifier.GetActiveOutboundCalls().ToList();

            Assert.Equal(2, activeCalls.Count);
            Assert.All(activeCalls, call => Assert.NotEqual(CallStatus.Ended, call.Status));
        }

        [Fact]
        public void CallHandlingStrategy_ShouldHaveCorrectValues() {

            Assert.Equal(0, (int)CallHandlingStrategy.Reject);
            Assert.Equal(1, (int)CallHandlingStrategy.WebToWeb);
            Assert.Equal(2, (int)CallHandlingStrategy.WebToNonWeb);
            Assert.Equal(3, (int)CallHandlingStrategy.NonWebToWeb);
            Assert.Equal(4, (int)CallHandlingStrategy.NonWebToNonWeb);
            Assert.Equal(5, (int)CallHandlingStrategy.Fallback);
        }

        [Fact]
        public void CallRoutingResult_ShouldCreateSuccessAndFailureCorrectly() {
            var mockLogger = new Mock<ILogger>();
            var mockSipClient = new SIPClient("test", mockLogger.Object, new Mock<SIPTransport>().Object);

            var successResult = CallRoutingResult.CreateSuccess(mockSipClient, null, CallHandlingStrategy.WebToWeb, "Success");
            var failureResult = CallRoutingResult.CreateFailure("Failure", CallHandlingStrategy.Reject);

            Assert.True(successResult.Success);
            Assert.Equal("Success", successResult.Message);
            Assert.Equal(CallHandlingStrategy.WebToWeb, successResult.Strategy);
            Assert.Equal(mockSipClient, successResult.TargetClient);

            Assert.False(failureResult.Success);
            Assert.Equal("Failure", failureResult.Message);
            Assert.Equal(CallHandlingStrategy.Reject, failureResult.Strategy);
            Assert.Null(failureResult.TargetClient);
        }

        public void Dispose() {
            _callTypeIdentifier?.Dispose();
        }
    }
}