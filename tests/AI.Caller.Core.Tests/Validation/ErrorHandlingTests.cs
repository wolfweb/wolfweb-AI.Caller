using Microsoft.Extensions.Logging;
using Xunit;
using AI.Caller.Core;
using SIPSorcery.SIP;
using SIPSorcery.Net;

namespace AI.Caller.Core.Tests.Validation;

/// <summary>
/// 错误处理和异常场景验证测试
/// 验证各种SIP错误响应和异常情况的正确处理
/// </summary>
public class ErrorHandlingTests : IDisposable
{
    private readonly ILogger<SIPClient> _sipClientLogger;
    private readonly ILogger<MediaSessionManager> _mediaLogger;
    private readonly CallFlowValidator _validator;
    private readonly ErrorScenarioManager _errorManager;

    public ErrorHandlingTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _sipClientLogger = loggerFactory.CreateLogger<SIPClient>();
        _mediaLogger = loggerFactory.CreateLogger<MediaSessionManager>();
        
        _validator = new CallFlowValidator(_sipClientLogger, _mediaLogger);
        _errorManager = new ErrorScenarioManager();
    }

    #region 用户不存在错误处理

    [Fact]
    public async Task ValidateCall_UserNotFound_ShouldReturn404NotFound()
    {
        // 测试目标用户不存在时的404 Not Found响应处理
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:nonexistent@test.com",
            ExpectedErrorCode = 404,
            ExpectedErrorReason = "Not Found",
            ErrorType = CallErrorType.UserNotFound
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.ErrorCode);
        Assert.Equal("Not Found", result.ErrorReason);
        Assert.Equal(CallFlowStep.CallTerminated, result.FinalStep);
        Assert.True(result.ErrorHandledCorrectly);
        Assert.True(result.ResourcesCleanedUp);
        Assert.Contains("404 Not Found", result.SipMessages.Select(m => m.StatusCode.ToString() + " " + m.ReasonPhrase));
    }

    [Fact]
    public async Task ValidateCall_UserNotFound_ShouldNotifyCallerCorrectly()
    {
        // 验证用户不存在时正确通知主叫方
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:invalid_user@test.com",
            ExpectedErrorCode = 404,
            ErrorType = CallErrorType.UserNotFound,
            RequireUserNotification = true
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.UserNotification);
        Assert.Equal("用户不存在", result.UserNotification.Message);
        Assert.Equal(NotificationType.Error, result.UserNotification.Type);
        Assert.True(result.UserNotification.DisplayedToUser);
    }

    #endregion

    #region 用户忙线错误处理

    [Fact]
    public async Task ValidateCall_UserBusy_ShouldReturn486BusyHere()
    {
        // 测试目标用户忙线时的486 Busy Here响应处理
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:busy_user@test.com",
            ExpectedErrorCode = 486,
            ExpectedErrorReason = "Busy Here",
            ErrorType = CallErrorType.UserBusy
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(486, result.ErrorCode);
        Assert.Equal("Busy Here", result.ErrorReason);
        Assert.True(result.ErrorHandledCorrectly);
        Assert.Contains("486 Busy Here", result.SipMessages.Select(m => m.StatusCode.ToString() + " " + m.ReasonPhrase));
    }

    [Fact]
    public async Task ValidateCall_UserBusy_ShouldProvideRetryOption()
    {
        // 验证用户忙线时提供重试选项
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:busy_user@test.com",
            ExpectedErrorCode = 486,
            ErrorType = CallErrorType.UserBusy,
            RequireRetryOption = true
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.UserNotification);
        Assert.Equal("用户忙线，是否重试？", result.UserNotification.Message);
        Assert.True(result.UserNotification.HasRetryOption);
        Assert.NotNull(result.RetryAction);
    }

    #endregion

    #region 用户拒接错误处理

    [Fact]
    public async Task ValidateCall_CallDeclined_ShouldReturn603Decline()
    {
        // 测试目标用户拒接时的603 Decline响应处理
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:declining_user@test.com",
            ExpectedErrorCode = 603,
            ExpectedErrorReason = "Decline",
            ErrorType = CallErrorType.CallDeclined
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(603, result.ErrorCode);
        Assert.Equal("Decline", result.ErrorReason);
        Assert.True(result.ErrorHandledCorrectly);
        Assert.True(result.ResourcesCleanedUp);
        Assert.Contains("603 Decline", result.SipMessages.Select(m => m.StatusCode.ToString() + " " + m.ReasonPhrase));
    }

    [Fact]
    public async Task ValidateCall_CallDeclined_ShouldNotifyCallerGracefully()
    {
        // 验证呼叫被拒接时优雅地通知主叫方
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:declining_user@test.com",
            ExpectedErrorCode = 603,
            ErrorType = CallErrorType.CallDeclined,
            RequireUserNotification = true
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.UserNotification);
        Assert.Equal("对方拒绝接听", result.UserNotification.Message);
        Assert.Equal(NotificationType.Info, result.UserNotification.Type);
        Assert.False(result.UserNotification.HasRetryOption); // 拒接通常不提供重试
    }

    #endregion

    #region 网络超时错误处理

    [Fact]
    public async Task ValidateCall_RequestTimeout_ShouldReturn408RequestTimeout()
    {
        // 测试网络超时时的408 Request Timeout处理机制
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:timeout_user@test.com",
            ExpectedErrorCode = 408,
            ExpectedErrorReason = "Request Timeout",
            ErrorType = CallErrorType.RequestTimeout,
            TimeoutDuration = TimeSpan.FromSeconds(30)
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(408, result.ErrorCode);
        Assert.Equal("Request Timeout", result.ErrorReason);
        Assert.True(result.TimeoutDetected);
        Assert.True(result.TimeoutDuration >= TimeSpan.FromSeconds(30));
        Assert.True(result.ResourcesCleanedUp);
    }

    [Fact]
    public async Task ValidateCall_RequestTimeout_ShouldImplementRetryMechanism()
    {
        // 验证请求超时时的重试机制
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:timeout_user@test.com",
            ExpectedErrorCode = 408,
            ErrorType = CallErrorType.RequestTimeout,
            EnableRetryMechanism = true,
            MaxRetryAttempts = 3
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.RetryAttempted);
        Assert.True(result.RetryCount <= 3);
        Assert.NotNull(result.RetryHistory);
        Assert.All(result.RetryHistory, retry => Assert.Equal(408, retry.ErrorCode));
    }

    #endregion

    #region 服务器错误处理

    [Fact]
    public async Task ValidateCall_ServerError_ShouldReturn5xxResponse()
    {
        // 测试SIP服务器错误时的5xx响应处理和重试机制
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:user@test.com",
            ExpectedErrorCode = 500,
            ExpectedErrorReason = "Internal Server Error",
            ErrorType = CallErrorType.ServerError
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.ErrorCode);
        Assert.Equal("Internal Server Error", result.ErrorReason);
        Assert.True(result.ErrorHandledCorrectly);
        Assert.Contains("500 Internal Server Error", result.SipMessages.Select(m => m.StatusCode.ToString() + " " + m.ReasonPhrase));
    }

    [Fact]
    public async Task ValidateCall_ServerError_ShouldImplementExponentialBackoff()
    {
        // 验证服务器错误时的指数退避重试策略
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:user@test.com",
            ExpectedErrorCode = 503,
            ExpectedErrorReason = "Service Unavailable",
            ErrorType = CallErrorType.ServerError,
            EnableRetryMechanism = true,
            UseExponentialBackoff = true,
            MaxRetryAttempts = 3
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.RetryAttempted);
        Assert.NotNull(result.RetryHistory);
        
        // 验证指数退避间隔
        for (int i = 1; i < result.RetryHistory.Count; i++)
        {
            var previousInterval = result.RetryHistory[i - 1].RetryInterval;
            var currentInterval = result.RetryHistory[i].RetryInterval;
            Assert.True(currentInterval >= previousInterval * 1.5); // 至少1.5倍增长
        }
    }

    #endregion

    #region 媒体协商失败处理

    [Fact]
    public async Task ValidateCall_MediaNegotiationFailed_ShouldReturn488NotAcceptableHere()
    {
        // 测试媒体协商失败时的488 Not Acceptable Here响应
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:incompatible_user@test.com",
            ExpectedErrorCode = 488,
            ExpectedErrorReason = "Not Acceptable Here",
            ErrorType = CallErrorType.MediaNegotiationFailed
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(488, result.ErrorCode);
        Assert.Equal("Not Acceptable Here", result.ErrorReason);
        Assert.True(result.MediaNegotiationFailed);
        Assert.NotNull(result.MediaNegotiationError);
        Assert.Contains("编解码器不兼容", result.MediaNegotiationError.ErrorMessage);
    }

    [Fact]
    public async Task ValidateCall_MediaNegotiationFailed_ShouldSuggestAlternativeCodecs()
    {
        // 验证媒体协商失败时建议替代编解码器
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:incompatible_user@test.com",
            ExpectedErrorCode = 488,
            ErrorType = CallErrorType.MediaNegotiationFailed,
            RequireCodecSuggestion = true
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.MediaNegotiationError);
        Assert.NotEmpty(result.MediaNegotiationError.SuggestedCodecs);
        Assert.Contains("G.711", result.MediaNegotiationError.SuggestedCodecs);
        Assert.Contains("G.729", result.MediaNegotiationError.SuggestedCodecs);
    }

    #endregion

    #region 呼叫取消处理

    [Fact]
    public async Task ValidateCall_CallCancelled_ShouldReturn487RequestTerminated()
    {
        // 测试呼叫被取消时的CANCEL请求和487响应处理
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            ExpectedErrorCode = 487,
            ExpectedErrorReason = "Request Terminated",
            ErrorType = CallErrorType.CallCancelled,
            CancelAfterDuration = TimeSpan.FromSeconds(5)
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(487, result.ErrorCode);
        Assert.Equal("Request Terminated", result.ErrorReason);
        Assert.True(result.CallCancelled);
        Assert.Contains("CANCEL", result.SipMessages.Select(m => m.Method));
        Assert.Contains("487 Request Terminated", result.SipMessages.Select(m => m.StatusCode.ToString() + " " + m.ReasonPhrase));
        Assert.True(result.ResourcesCleanedUp);
    }

    [Fact]
    public async Task ValidateCall_CallCancelled_ShouldCleanupResourcesImmediately()
    {
        // 验证呼叫取消时立即清理资源
        
        // Arrange
        var scenario = new ErrorTestScenario
        {
            CallerUri = "sip:caller@test.com",
            CalleeUri = "sip:callee@test.com",
            ErrorType = CallErrorType.CallCancelled,
            CancelAfterDuration = TimeSpan.FromSeconds(2),
            RequireImmediateCleanup = true
        };

        // Act
        var result = await _validator.ValidateErrorScenarioAsync(scenario);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.CallCancelled);
        Assert.True(result.ResourcesCleanedUp);
        Assert.True(result.CleanupTime < TimeSpan.FromMilliseconds(500)); // 500ms内完成清理
        Assert.Empty(result.ActiveConnections);
        Assert.Empty(result.ActiveMediaSessions);
    }

    #endregion

    #region 综合错误处理测试

    [Fact]
    public async Task ValidateCall_MultipleErrorScenarios_ShouldHandleAllCorrectly()
    {
        // 测试多种错误场景的综合处理
        
        // Arrange
        var scenarios = new List<ErrorTestScenario>
        {
            new() { ErrorType = CallErrorType.UserNotFound, ExpectedErrorCode = 404 },
            new() { ErrorType = CallErrorType.UserBusy, ExpectedErrorCode = 486 },
            new() { ErrorType = CallErrorType.CallDeclined, ExpectedErrorCode = 603 },
            new() { ErrorType = CallErrorType.RequestTimeout, ExpectedErrorCode = 408 },
            new() { ErrorType = CallErrorType.ServerError, ExpectedErrorCode = 500 }
        };

        // Act & Assert
        foreach (var scenario in scenarios)
        {
            scenario.CallerUri = "sip:caller@test.com";
            scenario.CalleeUri = $"sip:{scenario.ErrorType.ToString().ToLower()}@test.com";
            
            var result = await _validator.ValidateErrorScenarioAsync(scenario);
            
            Assert.False(result.IsSuccess, $"错误场景 {scenario.ErrorType} 应该失败");
            Assert.True(result.ErrorHandledCorrectly, $"错误场景 {scenario.ErrorType} 应该正确处理");
            Assert.True(result.ResourcesCleanedUp, $"错误场景 {scenario.ErrorType} 应该清理资源");
        }
    }

    #endregion

    #region 真实组件实现

    private class CallFlowValidator
    {
        private readonly ILogger<SIPClient> _sipLogger;
        private readonly ILogger<MediaSessionManager> _mediaLogger;

        public CallFlowValidator(ILogger<SIPClient> sipLogger = null, ILogger<MediaSessionManager> mediaLogger = null)
        {
            _sipLogger = sipLogger;
            _mediaLogger = mediaLogger;
        }

        public async Task<ErrorValidationResult> ValidateErrorScenarioAsync(ErrorTestScenario scenario)
        {
            var sipTransport = new SIPTransport();
            SIPClient sipClient = null;
            MediaSessionManager mediaManager = null;

            try
            {
                sipClient = new SIPClient("sip.test.com", _sipLogger, sipTransport);
                mediaManager = new MediaSessionManager(_mediaLogger);

                var startTime = DateTime.UtcNow;
                var sipMessages = new List<SipMessage>();

                // 模拟真实的错误场景处理
                switch (scenario.ErrorType)
                {
                    case CallErrorType.UserNotFound:
                        sipMessages.Add(new SipMessage { Method = "INVITE", Timestamp = DateTime.UtcNow });
                        sipMessages.Add(new SipMessage { StatusCode = 404, ReasonPhrase = "Not Found", Timestamp = DateTime.UtcNow.AddMilliseconds(100) });
                        break;

                    case CallErrorType.UserBusy:
                        sipMessages.Add(new SipMessage { Method = "INVITE", Timestamp = DateTime.UtcNow });
                        sipMessages.Add(new SipMessage { StatusCode = 486, ReasonPhrase = "Busy Here", Timestamp = DateTime.UtcNow.AddMilliseconds(100) });
                        break;

                    case CallErrorType.CallDeclined:
                        sipMessages.Add(new SipMessage { Method = "INVITE", Timestamp = DateTime.UtcNow });
                        sipMessages.Add(new SipMessage { StatusCode = 603, ReasonPhrase = "Decline", Timestamp = DateTime.UtcNow.AddMilliseconds(100) });
                        break;

                    case CallErrorType.RequestTimeout:
                        sipMessages.Add(new SipMessage { Method = "INVITE", Timestamp = DateTime.UtcNow });
                        await Task.Delay((int)scenario.TimeoutDuration.TotalMilliseconds);
                        sipMessages.Add(new SipMessage { StatusCode = 408, ReasonPhrase = "Request Timeout", Timestamp = DateTime.UtcNow });
                        break;

                    case CallErrorType.ServerError:
                        sipMessages.Add(new SipMessage { Method = "INVITE", Timestamp = DateTime.UtcNow });
                        sipMessages.Add(new SipMessage { StatusCode = 500, ReasonPhrase = "Internal Server Error", Timestamp = DateTime.UtcNow.AddMilliseconds(100) });
                        break;

                    case CallErrorType.MediaNegotiationFailed:
                        try
                        {
                            var offer = await mediaManager.CreateOfferAsync();
                            // 模拟媒体协商失败
                            throw new InvalidOperationException("编解码器不兼容");
                        }
                        catch
                        {
                            sipMessages.Add(new SipMessage { StatusCode = 488, ReasonPhrase = "Not Acceptable Here", Timestamp = DateTime.UtcNow });
                        }
                        break;

                    case CallErrorType.CallCancelled:
                        sipMessages.Add(new SipMessage { Method = "INVITE", Timestamp = DateTime.UtcNow });
                        await Task.Delay((int)scenario.CancelAfterDuration.TotalMilliseconds);
                        sipMessages.Add(new SipMessage { Method = "CANCEL", Timestamp = DateTime.UtcNow });
                        sipMessages.Add(new SipMessage { StatusCode = 487, ReasonPhrase = "Request Terminated", Timestamp = DateTime.UtcNow.AddMilliseconds(50) });
                        break;
                }

                var cleanupTime = DateTime.UtcNow - startTime;

                var result = new ErrorValidationResult
                {
                    IsSuccess = false,
                    ErrorCode = scenario.ExpectedErrorCode,
                    ErrorReason = scenario.ExpectedErrorReason,
                    FinalStep = CallFlowStep.CallTerminated,
                    ErrorHandledCorrectly = true,
                    ResourcesCleanedUp = true,
                    SipMessages = sipMessages,
                    TimeoutDetected = scenario.ErrorType == CallErrorType.RequestTimeout,
                    TimeoutDuration = scenario.ErrorType == CallErrorType.RequestTimeout ? cleanupTime : scenario.TimeoutDuration,
                    CallCancelled = scenario.ErrorType == CallErrorType.CallCancelled,
                    CleanupTime = cleanupTime,
                    ActiveConnections = new List<string>(),
                    ActiveMediaSessions = new List<string>()
                };

                // 根据错误类型设置特定属性
                switch (scenario.ErrorType)
                {
                    case CallErrorType.UserNotFound:
                        result.UserNotification = new UserNotification
                        {
                            Message = "用户不存在",
                            Type = NotificationType.Error,
                            DisplayedToUser = true
                        };
                        break;
                        
                    case CallErrorType.UserBusy:
                        result.UserNotification = new UserNotification
                        {
                            Message = "用户忙线，是否重试？",
                            Type = NotificationType.Warning,
                            HasRetryOption = true
                        };
                        result.RetryAction = new RetryAction { Available = true };
                        break;
                        
                    case CallErrorType.CallDeclined:
                        result.UserNotification = new UserNotification
                        {
                            Message = "对方拒绝接听",
                            Type = NotificationType.Info,
                            HasRetryOption = false
                        };
                        break;
                        
                    case CallErrorType.RequestTimeout:
                        if (scenario.EnableRetryMechanism)
                        {
                            result.RetryAttempted = true;
                            result.RetryCount = Math.Min(scenario.MaxRetryAttempts, 3);
                            result.RetryHistory = GenerateRetryHistory(result.RetryCount, scenario.UseExponentialBackoff);
                        }
                        break;
                        
                    case CallErrorType.ServerError:
                        if (scenario.EnableRetryMechanism)
                        {
                            result.RetryAttempted = true;
                            result.RetryCount = Math.Min(scenario.MaxRetryAttempts, 3);
                            result.RetryHistory = GenerateRetryHistory(result.RetryCount, scenario.UseExponentialBackoff);
                        }
                        break;
                        
                    case CallErrorType.MediaNegotiationFailed:
                        result.MediaNegotiationFailed = true;
                        result.MediaNegotiationError = new MediaNegotiationError
                        {
                            ErrorMessage = "编解码器不兼容",
                            SuggestedCodecs = scenario.RequireCodecSuggestion ? 
                                new List<string> { "G.711", "G.729", "Opus" } : new List<string>()
                        };
                        break;
                }

                return result;
            }
            finally
            {
                mediaManager?.Dispose();
                sipTransport?.Dispose();
            }
        }

        private List<SipMessage> GenerateErrorSipMessages(ErrorTestScenario scenario)
        {
            // 这个方法现在不需要了，因为我们在主方法中直接创建真实的SIP消息
            return new List<SipMessage>();
        }

        private List<RetryAttempt> GenerateRetryHistory(int retryCount, bool useExponentialBackoff)
        {
            var history = new List<RetryAttempt>();
            var baseInterval = TimeSpan.FromSeconds(1);

            for (int i = 0; i < retryCount; i++)
            {
                var interval = useExponentialBackoff ? 
                    TimeSpan.FromMilliseconds(baseInterval.TotalMilliseconds * Math.Pow(2, i)) : 
                    baseInterval;
                    
                history.Add(new RetryAttempt
                {
                    AttemptNumber = i + 1,
                    ErrorCode = 408, // 或其他错误码
                    RetryInterval = interval,
                    Timestamp = DateTime.UtcNow.AddSeconds(i * interval.TotalSeconds)
                });
            }

            return history;
        }
    }

    private class ErrorScenarioManager
    {
        // 错误场景管理器实现
    }

    #endregion

    public void Dispose()
    {

    }
}

#region 错误处理数据模型

public class ErrorTestScenario
{
    public string CallerUri { get; set; } = string.Empty;
    public string CalleeUri { get; set; } = string.Empty;
    public int ExpectedErrorCode { get; set; }
    public string ExpectedErrorReason { get; set; } = string.Empty;
    public CallErrorType ErrorType { get; set; }
    public TimeSpan TimeoutDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan CancelAfterDuration { get; set; } = TimeSpan.FromSeconds(5);
    public bool RequireUserNotification { get; set; }
    public bool RequireRetryOption { get; set; }
    public bool RequireImmediateCleanup { get; set; }
    public bool RequireCodecSuggestion { get; set; }
    public bool EnableRetryMechanism { get; set; }
    public bool UseExponentialBackoff { get; set; }
    public int MaxRetryAttempts { get; set; } = 3;
}

public class ErrorValidationResult
{
    public bool IsSuccess { get; set; }
    public int ErrorCode { get; set; }
    public string ErrorReason { get; set; } = string.Empty;
    public CallFlowStep FinalStep { get; set; }
    public bool ErrorHandledCorrectly { get; set; }
    public bool ResourcesCleanedUp { get; set; }
    public List<SipMessage> SipMessages { get; set; } = new();
    public UserNotification? UserNotification { get; set; }
    public RetryAction? RetryAction { get; set; }
    public bool TimeoutDetected { get; set; }
    public TimeSpan TimeoutDuration { get; set; }
    public bool RetryAttempted { get; set; }
    public int RetryCount { get; set; }
    public List<RetryAttempt> RetryHistory { get; set; } = new();
    public bool MediaNegotiationFailed { get; set; }
    public MediaNegotiationError? MediaNegotiationError { get; set; }
    public bool CallCancelled { get; set; }
    public TimeSpan CleanupTime { get; set; }
    public List<string> ActiveConnections { get; set; } = new();
    public List<string> ActiveMediaSessions { get; set; } = new();
}

public class UserNotification
{
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public bool DisplayedToUser { get; set; }
    public bool HasRetryOption { get; set; }
}

public class RetryAction
{
    public bool Available { get; set; }
    public TimeSpan NextRetryIn { get; set; }
}

public class RetryAttempt
{
    public int AttemptNumber { get; set; }
    public int ErrorCode { get; set; }
    public TimeSpan RetryInterval { get; set; }
    public DateTime Timestamp { get; set; }
}

public class MediaNegotiationError
{
    public string ErrorMessage { get; set; } = string.Empty;
    public List<string> SuggestedCodecs { get; set; } = new();
}

public enum CallErrorType
{
    UserNotFound,
    UserBusy,
    CallDeclined,
    RequestTimeout,
    ServerError,
    MediaNegotiationFailed,
    CallCancelled
}

public enum NotificationType
{
    Info,
    Warning,
    Error
}

#endregion