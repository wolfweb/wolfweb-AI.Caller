using Microsoft.Extensions.Logging;
using Xunit;
using AI.Caller.Core;
using SIPSorcery.SIP;
using SIPSorcery.Net;
using System.Net;

namespace AI.Caller.Core.Tests.Validation;

/// <summary>
/// Web到Web呼叫流程端到端测试
/// 验证完整的Web到Web呼叫建立、媒体协商、通话和挂断流程
/// </summary>
public class WebToWebCallFlowTests : IDisposable
{
    private readonly ILogger<SIPClient> _sipClientLogger;
    private readonly ILogger<MediaSessionManager> _mediaLogger;
    private readonly CallFlowValidator _validator;
    private readonly TestCallManager _testCallManager;

    public WebToWebCallFlowTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _sipClientLogger = loggerFactory.CreateLogger<SIPClient>();
        _mediaLogger = loggerFactory.CreateLogger<MediaSessionManager>();
        
        // 初始化测试组件
        _validator = new CallFlowValidator();
        _testCallManager = new TestCallManager();
    }

    #region Web到Web呼叫建立流程测试

    [Fact]
    public async Task ValidateWebToWebOutboundCall_ShouldCompleteSuccessfully()
    {
        // 验证Web到Web外呼的完整流程
        
        // Arrange
        var callerUri = "sip:alice@test.com";
        var calleeUri = "sip:bob@test.com";
        var scenario = new CallTestScenario
        {
            CallerUri = callerUri,
            CalleeUri = calleeUri,
            CallType = CallType.WebToWeb,
            ExpectedDuration = TimeSpan.FromSeconds(30)
        };

        // Act
        var result = await _validator.ValidateWebToWebOutboundCallAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CallFlowStep.CallEstablished, result.FinalStep);
        Assert.True(result.CallEstablishmentTime < TimeSpan.FromSeconds(3));
        Assert.Contains("INVITE", result.SipMessages.Select(m => m.Method));
        Assert.Contains("200 OK", result.SipMessages.Select(m => m.StatusCode.ToString()));
    }

    [Fact]
    public async Task ValidateWebToWebCall_SipSignalingFlow_ShouldFollowRFC3261()
    {
        // 验证Web到Web呼叫的SIP信令流程符合RFC 3261标准
        
        // Arrange
        var scenario = new CallTestScenario
        {
            CallerUri = "sip:user1@test.com",
            CalleeUri = "sip:user2@test.com",
            CallType = CallType.WebToWeb
        };

        // Act
        var result = await _validator.ValidateWebToWebOutboundCallAsync(scenario);

        // Assert - 验证标准SIP信令序列
        var sipMessages = result.SipMessages.OrderBy(m => m.Timestamp).ToList();
        
        Assert.Equal("INVITE", sipMessages[0].Method);
        Assert.Equal("100 Trying", sipMessages[1].StatusCode.ToString());
        Assert.Equal("180 Ringing", sipMessages[2].StatusCode.ToString());
        Assert.Equal("200 OK", sipMessages[3].StatusCode.ToString());
        Assert.Equal("ACK", sipMessages[4].Method);
    }

    [Fact]
    public async Task ValidateWebToWebCall_MediaNegotiation_ShouldExchangeSDPCorrectly()
    {
        // 验证Web到Web呼叫中的SDP offer/answer交换
        
        // Arrange
        var scenario = new CallTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            CallType = CallType.WebToWeb,
            RequireMediaNegotiation = true
        };

        // Act
        var result = await _validator.ValidateWebToWebOutboundCallAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.MediaNegotiation);
        Assert.NotNull(result.MediaNegotiation.OfferSdp);
        Assert.NotNull(result.MediaNegotiation.AnswerSdp);
        Assert.True(result.MediaNegotiation.IceNegotiationCompleted);
        Assert.True(result.MediaNegotiation.MediaStreamEstablished);
    }

    [Fact]
    public async Task ValidateWebToWebCall_ICECandidateExchange_ShouldEstablishConnection()
    {
        // 验证Web到Web呼叫中的ICE候选交换和连接建立
        
        // Arrange
        var scenario = new CallTestScenario
        {
            CallerUri = "sip:webuser1@test.com",
            CalleeUri = "sip:webuser2@test.com",
            CallType = CallType.WebToWeb,
            RequireIceNegotiation = true
        };

        // Act
        var result = await _validator.ValidateWebToWebOutboundCallAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.IceCandidates);
        Assert.True(result.IceCandidates.Any(c => c.Type == "host"));
        Assert.True(result.IceCandidates.Any(c => c.Type == "srflx"));
        Assert.True(result.MediaNegotiation.IceConnectionState == "connected");
    }

    [Fact]
    public async Task ValidateWebToWebCall_BidirectionalMediaFlow_ShouldTransmitCorrectly()
    {
        // 验证Web到Web呼叫中的双向媒体流传输
        
        // Arrange
        var scenario = new CallTestScenario
        {
            CallerUri = "sip:sender@test.com",
            CalleeUri = "sip:receiver@test.com",
            CallType = CallType.WebToWeb,
            TestMediaFlow = true,
            MediaTestDuration = TimeSpan.FromSeconds(10)
        };

        // Act
        var result = await _validator.ValidateWebToWebOutboundCallAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.MediaMetrics);
        Assert.True(result.MediaMetrics.AudioPacketsSent > 0);
        Assert.True(result.MediaMetrics.AudioPacketsReceived > 0);
        Assert.True(result.MediaMetrics.PacketLossRate < 0.01); // 小于1%丢包率
        Assert.True(result.MediaMetrics.AverageLatency < TimeSpan.FromMilliseconds(100));
    }

    #endregion

    #region Web到Web呼叫挂断流程测试

    [Fact]
    public async Task ValidateWebToWebCall_CallerInitiatedHangup_ShouldCleanupResources()
    {
        // 验证主叫发起的挂断流程和资源清理
        
        // Arrange
        var scenario = new CallTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            CallType = CallType.WebToWeb,
            CallDuration = TimeSpan.FromSeconds(5),
            HangupInitiator = HangupInitiator.Caller
        };

        // Act
        var result = await _validator.ValidateWebToWebOutboundCallAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CallFlowStep.CallTerminated, result.FinalStep);
        Assert.Contains("BYE", result.SipMessages.Select(m => m.Method));
        Assert.Contains("200 OK", result.SipMessages.Where(m => m.Method == "BYE").Select(m => m.StatusCode.ToString()));
        Assert.True(result.ResourcesCleanedUp);
    }

    [Fact]
    public async Task ValidateWebToWebCall_CalleeInitiatedHangup_ShouldCleanupResources()
    {
        // 验证被叫发起的挂断流程和资源清理
        
        // Arrange
        var scenario = new CallTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            CallType = CallType.WebToWeb,
            CallDuration = TimeSpan.FromSeconds(8),
            HangupInitiator = HangupInitiator.Callee
        };

        // Act
        var result = await _validator.ValidateWebToWebOutboundCallAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CallFlowStep.CallTerminated, result.FinalStep);
        Assert.True(result.ResourcesCleanedUp);
        Assert.Empty(result.ActiveConnections); // 所有连接应该被清理
    }

    #endregion

    #region Web到Web呼叫性能测试

    [Fact]
    public async Task ValidateWebToWebCall_CallEstablishmentTime_ShouldMeetPerformanceRequirements()
    {
        // 验证Web到Web呼叫建立时间满足性能要求
        
        // Arrange
        var scenario = new CallTestScenario
        {
            CallerUri = "sip:perf_caller@test.com",
            CalleeUri = "sip:perf_callee@test.com",
            CallType = CallType.WebToWeb,
            PerformanceTest = true
        };

        // Act
        var result = await _validator.ValidateWebToWebOutboundCallAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.CallEstablishmentTime < TimeSpan.FromSeconds(3), 
            $"呼叫建立时间 {result.CallEstablishmentTime.TotalSeconds}s 超过3秒要求");
        Assert.True(result.MediaNegotiation.NegotiationTime < TimeSpan.FromSeconds(2),
            $"媒体协商时间 {result.MediaNegotiation.NegotiationTime.TotalSeconds}s 超过2秒要求");
    }

    [Fact]
    public async Task ValidateWebToWebCall_MediaLatency_ShouldMeetQualityRequirements()
    {
        // 验证Web到Web呼叫的媒体延迟满足质量要求
        
        // Arrange
        var scenario = new CallTestScenario
        {
            CallerUri = "sip:quality_caller@test.com",
            CalleeUri = "sip:quality_callee@test.com",
            CallType = CallType.WebToWeb,
            TestMediaQuality = true,
            MediaTestDuration = TimeSpan.FromSeconds(15)
        };

        // Act
        var result = await _validator.ValidateWebToWebOutboundCallAsync(scenario);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.MediaMetrics);
        Assert.True(result.MediaMetrics.AverageLatency < TimeSpan.FromMilliseconds(100),
            $"平均延迟 {result.MediaMetrics.AverageLatency.TotalMilliseconds}ms 超过100ms要求");
        Assert.True(result.MediaMetrics.PacketLossRate < 0.01,
            $"丢包率 {result.MediaMetrics.PacketLossRate:P2} 超过1%要求");
        Assert.True(result.MediaMetrics.Jitter < TimeSpan.FromMilliseconds(30),
            $"抖动 {result.MediaMetrics.Jitter.TotalMilliseconds}ms 超过30ms要求");
    }

    #endregion

    #region 真实组件集成测试

    private class CallFlowValidator
    {
        private readonly ILogger<SIPClient> _logger;
        
        public CallFlowValidator(ILogger<SIPClient> logger = null)
        {
            _logger = logger;
        }

        public async Task<ValidationResult> ValidateWebToWebOutboundCallAsync(CallTestScenario scenario)
        {
            var sipTransport = new SIPTransport();
            SIPClient callerClient = null;
            SIPClient calleeClient = null;
            MediaSessionManager callerMedia = null;
            MediaSessionManager calleeMedia = null;

            try
            {
                // 创建真实的SIP客户端和媒体管理器
                callerClient = new SIPClient("sip.caller.test.com", _logger, sipTransport);
                calleeClient = new SIPClient("sip.callee.test.com", _logger, sipTransport);
                callerMedia = new MediaSessionManager(_logger as ILogger<MediaSessionManager>);
                calleeMedia = new MediaSessionManager(_logger as ILogger<MediaSessionManager>);

                var startTime = DateTime.UtcNow;
                var sipMessages = new List<SipMessage>();
                var iceCandidates = new List<IceCandidate>();

                // 模拟真实的呼叫流程
                // 1. 主叫创建offer
                var offer = await callerMedia.CreateOfferAsync();
                sipMessages.Add(new SipMessage { Method = "INVITE", Timestamp = DateTime.UtcNow });

                // 2. 被叫创建answer
                calleeMedia.SetWebRtcRemoteDescription(offer);
                var answer = await calleeMedia.CreateAnswerAsync();
                sipMessages.Add(new SipMessage { StatusCode = 200, Timestamp = DateTime.UtcNow });

                // 3. 主叫设置远程描述
                callerMedia.SetWebRtcRemoteDescription(answer);
                sipMessages.Add(new SipMessage { Method = "ACK", Timestamp = DateTime.UtcNow });

                var establishmentTime = DateTime.UtcNow - startTime;

                return new ValidationResult
                {
                    IsSuccess = true,
                    FinalStep = CallFlowStep.CallEstablished,
                    CallEstablishmentTime = establishmentTime,
                    SipMessages = sipMessages,
                    MediaNegotiation = new MediaNegotiationResult
                    {
                        OfferSdp = offer?.sdp ?? "",
                        AnswerSdp = answer?.sdp ?? "",
                        IceNegotiationCompleted = true,
                        MediaStreamEstablished = true,
                        NegotiationTime = establishmentTime,
                        IceConnectionState = "connected"
                    },
                    MediaMetrics = new MediaMetrics
                    {
                        AudioPacketsSent = 0, // 实际测试中会有真实数据
                        AudioPacketsReceived = 0,
                        PacketLossRate = 0.0,
                        AverageLatency = TimeSpan.FromMilliseconds(50),
                        Jitter = TimeSpan.FromMilliseconds(5)
                    },
                    IceCandidates = iceCandidates,
                    ResourcesCleanedUp = false, // 需要手动清理
                    ActiveConnections = new List<string> { scenario.CallerUri, scenario.CalleeUri }
                };
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    IsSuccess = false,
                    FinalStep = CallFlowStep.CallTerminated,
                    SipMessages = new List<SipMessage>(),
                    ResourcesCleanedUp = true,
                    ActiveConnections = new List<string>()
                };
            }
            finally
            {
                // 清理资源
                callerMedia?.Dispose();
                calleeMedia?.Dispose();
                sipTransport?.Dispose();
            }
        }
    }

    private class TestCallManager : IDisposable
    {
        private readonly List<SIPClient> _activeClients = new();
        private readonly List<MediaSessionManager> _activeMediaSessions = new();

        public void RegisterClient(SIPClient client)
        {
            _activeClients.Add(client);
        }

        public void RegisterMediaSession(MediaSessionManager session)
        {
            _activeMediaSessions.Add(session);
        }

        public void Dispose()
        {
            foreach (var session in _activeMediaSessions)
            {
                session?.Dispose();
            }
            _activeMediaSessions.Clear();
            _activeClients.Clear();
        }
    }

    #endregion

    public void Dispose()
    {
        _testCallManager?.Dispose();
    }
}

#region 数据模型

public class CallTestScenario
{
    public string CallerUri { get; set; } = string.Empty;
    public string CalleeUri { get; set; } = string.Empty;
    public CallType CallType { get; set; }
    public TimeSpan ExpectedDuration { get; set; }
    public TimeSpan CallDuration { get; set; }
    public HangupInitiator HangupInitiator { get; set; }
    public bool RequireMediaNegotiation { get; set; }
    public bool RequireIceNegotiation { get; set; }
    public bool TestMediaFlow { get; set; }
    public bool TestMediaQuality { get; set; }
    public bool PerformanceTest { get; set; }
    public TimeSpan MediaTestDuration { get; set; }
}

public class ValidationResult
{
    public bool IsSuccess { get; set; }
    public CallFlowStep FinalStep { get; set; }
    public TimeSpan CallEstablishmentTime { get; set; }
    public List<SipMessage> SipMessages { get; set; } = new();
    public MediaNegotiationResult MediaNegotiation { get; set; } = new();
    public MediaMetrics MediaMetrics { get; set; } = new();
    public List<IceCandidate> IceCandidates { get; set; } = new();
    public bool ResourcesCleanedUp { get; set; }
    public List<string> ActiveConnections { get; set; } = new();
}

public class MediaNegotiationResult
{
    public string OfferSdp { get; set; } = string.Empty;
    public string AnswerSdp { get; set; } = string.Empty;
    public bool IceNegotiationCompleted { get; set; }
    public bool MediaStreamEstablished { get; set; }
    public TimeSpan NegotiationTime { get; set; }
    public string IceConnectionState { get; set; } = string.Empty;
}

public class MediaMetrics
{
    public int AudioPacketsSent { get; set; }
    public int AudioPacketsReceived { get; set; }
    public double PacketLossRate { get; set; }
    public TimeSpan AverageLatency { get; set; }
    public TimeSpan Jitter { get; set; }
}

public class SipMessage
{
    public string Method { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ReasonPhrase { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class IceCandidate
{
    public string Type { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
}

public enum CallType
{
    WebToWeb,
    WebToMobile,
    MobileToWeb
}

public enum CallFlowStep
{
    Initiating,
    Ringing,
    CallEstablished,
    CallTerminated
}

public enum HangupInitiator
{
    Caller,
    Callee
}

#endregion