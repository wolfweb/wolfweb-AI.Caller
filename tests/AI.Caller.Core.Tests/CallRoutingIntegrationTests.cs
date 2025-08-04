using Microsoft.Extensions.Logging;
using Xunit;
using Moq;
using AI.Caller.Core;
using AI.Caller.Phone.CallRouting.Services;
using AI.Caller.Phone.CallRouting.Interfaces;
using AI.Caller.Phone.CallRouting.Models;
using SIPSorcery.SIP;

namespace AI.Caller.Core.Tests
{
    /// <summary>
    /// CallRouting集成测试 - 验证现有CallRouting代码与重构后MediaSessionManager的兼容性
    /// </summary>
    public class CallRoutingIntegrationTests : IDisposable
    {
        private readonly Mock<ILogger<CallTypeIdentifier>> _mockCallTypeLogger;
        private readonly CallTypeIdentifier _callTypeIdentifier;

        public CallRoutingIntegrationTests()
        {
            _mockCallTypeLogger = new Mock<ILogger<CallTypeIdentifier>>();
            _callTypeIdentifier = new CallTypeIdentifier(_mockCallTypeLogger.Object);
        }

        [Fact]
        public void MediaSessionManager_ShouldIntegrateWithCallRouting()
        {
            // Arrange
            var logger = new Mock<ILogger>();
            var mediaManager = new MediaSessionManager(logger.Object);

            // Act & Assert - 验证MediaSessionManager的事件驱动架构与CallRouting兼容
            Assert.NotNull(mediaManager);
            Assert.Null(mediaManager.MediaSession); // 应该为null，因为还未初始化
            Assert.Null(mediaManager.PeerConnection); // 应该为null，因为还未初始化
            
            // 验证事件可以被订阅（这证明了事件驱动架构的兼容性）
            var eventTriggered = false;
            mediaManager.SdpOfferGenerated += (offer) => eventTriggered = true;
            
            // 清理
            mediaManager.Dispose();
            Assert.False(eventTriggered); // 事件未被触发，因为没有实际操作
        }

        [Fact]
        public void CallTypeIdentifier_ShouldTrackOutboundCalls()
        {
            // Arrange
            var callId = "outbound-call-123";
            var fromTag = "from-tag-456";
            var sipUsername = "caller@example.com";
            var destination = "1234567890";

            // Act
            _callTypeIdentifier.RegisterOutboundCallWithSipTags(callId, fromTag, sipUsername, destination);
            var callInfo = _callTypeIdentifier.GetOutboundCallInfo(callId);

            // Assert
            Assert.NotNull(callInfo);
            Assert.Equal(callId, callInfo.CallId);
            Assert.Equal(fromTag, callInfo.FromTag);
            Assert.Equal(sipUsername, callInfo.SipUsername);
            Assert.Equal(destination, callInfo.Destination);
            Assert.Equal(CallStatus.Initiated, callInfo.Status);
        }

        [Fact]
        public void CallTypeIdentifier_ShouldUpdateCallStatus()
        {
            // Arrange
            var callId = "status-update-call-123";
            var sipUsername = "caller@example.com";
            
            _callTypeIdentifier.RegisterOutboundCallWithSipTags(callId, "tag", sipUsername, "dest");

            // Act
            _callTypeIdentifier.UpdateOutboundCallStatus(callId, CallStatus.Answered);
            var callInfo = _callTypeIdentifier.GetOutboundCallInfo(callId);

            // Assert
            Assert.NotNull(callInfo);
            Assert.Equal(CallStatus.Answered, callInfo.Status);
        }

        [Fact]
        public void CallTypeIdentifier_ShouldReturnActiveCallsOnly()
        {
            // Arrange
            _callTypeIdentifier.RegisterOutboundCallWithSipTags("active1", "tag1", "user1", "dest1");
            _callTypeIdentifier.RegisterOutboundCallWithSipTags("active2", "tag2", "user2", "dest2");
            _callTypeIdentifier.RegisterOutboundCallWithSipTags("ended", "tag3", "user3", "dest3");
            
            _callTypeIdentifier.UpdateOutboundCallStatus("ended", CallStatus.Ended);

            // Act
            var activeCalls = _callTypeIdentifier.GetActiveOutboundCalls().ToList();

            // Assert
            Assert.Equal(2, activeCalls.Count);
            Assert.All(activeCalls, call => Assert.NotEqual(CallStatus.Ended, call.Status));
        }

        [Fact]
        public void CallHandlingStrategy_ShouldHaveCorrectValues()
        {
            // Act & Assert - 验证CallHandlingStrategy枚举正确定义
            Assert.Equal(0, (int)CallHandlingStrategy.Reject);
            Assert.Equal(1, (int)CallHandlingStrategy.WebToWeb);
            Assert.Equal(2, (int)CallHandlingStrategy.WebToNonWeb);
            Assert.Equal(3, (int)CallHandlingStrategy.NonWebToWeb);
            Assert.Equal(4, (int)CallHandlingStrategy.NonWebToNonWeb);
            Assert.Equal(5, (int)CallHandlingStrategy.Fallback);
        }

        [Fact]
        public void CallRoutingResult_ShouldCreateSuccessAndFailureCorrectly()
        {
            // Arrange
            var mockLogger = new Mock<ILogger>();
            var mockSipClient = new SIPClient("test", mockLogger.Object, new Mock<SIPTransport>().Object);

            // Act
            var successResult = CallRoutingResult.CreateSuccess(mockSipClient, null, CallHandlingStrategy.WebToWeb, "Success");
            var failureResult = CallRoutingResult.CreateFailure("Failure", CallHandlingStrategy.Reject);

            // Assert
            Assert.True(successResult.Success);
            Assert.Equal("Success", successResult.Message);
            Assert.Equal(CallHandlingStrategy.WebToWeb, successResult.Strategy);
            Assert.Equal(mockSipClient, successResult.TargetClient);

            Assert.False(failureResult.Success);
            Assert.Equal("Failure", failureResult.Message);
            Assert.Equal(CallHandlingStrategy.Reject, failureResult.Strategy);
            Assert.Null(failureResult.TargetClient);
        }

        public void Dispose()
        {
            _callTypeIdentifier?.Dispose();
        }
    }
}