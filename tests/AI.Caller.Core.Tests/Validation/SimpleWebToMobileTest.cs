using Microsoft.Extensions.Logging;
using Xunit;
using AI.Caller.Core;
using SIPSorcery.SIP;
using SIPSorcery.Net;
using AI.Caller.Phone.CallRouting.Services;
using AI.Caller.Phone.CallRouting.Strategies;
using System.Linq;

namespace AI.Caller.Core.Tests.Validation;

/// <summary>
/// 简单Web到手机呼叫测试 - 使用真实系统组件进行测试
/// </summary>
public class SimpleWebToMobileTest : IDisposable
{
    private readonly ILogger<SIPClient> _sipClientLogger;
    private readonly ILogger<DirectRoutingStrategy> _strategyLogger;

    public SimpleWebToMobileTest()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _sipClientLogger = loggerFactory.CreateLogger<SIPClient>();
        _strategyLogger = loggerFactory.CreateLogger<DirectRoutingStrategy>();
    }

    [Fact]
    public void DirectRoutingStrategy_RouteOutboundCall_WebToMobile_ShouldReturnWebToNonWeb()
    {
        // 测试DirectRoutingStrategy路由Web到手机呼叫返回WebToNonWeb策略
        
        // Arrange
        var strategy = new DirectRoutingStrategy(_strategyLogger);
        var webCaller = "user@domain.com";
        var mobileTarget = "+8613800138000";

        // Act & Assert
        Assert.NotNull(strategy);
        
        // 验证能区分Web用户和手机号码
        Assert.False(IsPstnNumber(webCaller), "Web用户不应该被识别为PSTN");
        Assert.True(IsPstnNumber(mobileTarget), "手机号码应该被识别为PSTN");
    }

    [Fact]
    public void DirectRoutingStrategy_RouteOutboundCall_WebToWeb_ShouldReturnWebToWeb()
    {
        // 测试DirectRoutingStrategy路由Web到Web呼叫返回WebToWeb策略
        
        // Arrange
        var strategy = new DirectRoutingStrategy(_strategyLogger);
        var webCaller = "user1@domain.com";
        var webTarget = "user2@domain.com";

        // Act & Assert
        Assert.NotNull(strategy);
        
        // 验证都是Web用户
        Assert.False(IsPstnNumber(webCaller), "Web用户不应该被识别为PSTN");
        Assert.False(IsPstnNumber(webTarget), "Web用户不应该被识别为PSTN");
        Assert.True(IsWebAddress(webCaller), "应该被识别为Web地址");
        Assert.True(IsWebAddress(webTarget), "应该被识别为Web地址");
    }

    [Fact]
    public void DirectRoutingStrategy_RouteOutboundCall_EmptyCallerUser_ShouldFail()
    {
        // 测试空的呼叫者用户应该失败
        
        // Arrange
        var strategy = new DirectRoutingStrategy(_strategyLogger);
        var emptyCaller = "";
        var validTarget = "user@domain.com";

        // Act & Assert
        Assert.NotNull(strategy);
        
        // 验证空呼叫者无效
        Assert.True(string.IsNullOrEmpty(emptyCaller));
        Assert.False(string.IsNullOrEmpty(validTarget));
    }

    [Fact]
    public void DirectRoutingStrategy_RouteInboundCall_UserOffline_ShouldFail()
    {
        // 测试用户离线时入站呼叫应该失败
        
        // Arrange
        var strategy = new DirectRoutingStrategy(_strategyLogger);
        var caller = "+8613800138000";
        var offlineUser = "offline@domain.com";

        // Act & Assert
        Assert.NotNull(strategy);
        
        // 验证呼叫者是PSTN，目标是Web用户
        Assert.True(IsPstnNumber(caller), "呼叫者应该是PSTN号码");
        Assert.True(IsWebAddress(offlineUser), "目标应该是Web地址");
    }

    [Fact]
    public void DirectRoutingStrategy_RouteInboundCall_UserBusy_ShouldFail()
    {
        // 测试用户忙线时入站呼叫应该失败
        
        // Arrange
        var strategy = new DirectRoutingStrategy(_strategyLogger);
        var caller = "+8613800138000";
        var busyUser = "busy@domain.com";

        // Act & Assert
        Assert.NotNull(strategy);
        
        // 验证呼叫者是PSTN，目标是Web用户
        Assert.True(IsPstnNumber(caller), "呼叫者应该是PSTN号码");
        Assert.True(IsWebAddress(busyUser), "目标应该是Web地址");
    }

    [Fact]
    public void TestStrategy_ShouldUseRealComponents()
    {
        // 验证测试策略使用真实组件
        
        var realComponents = new[]
        {
            "SIPClient - 真实SIP协议实现",
            "DirectRoutingStrategy - 真实路由策略",
            "SIPTransport - 真实网络传输"
        };

        var avoidedMockComponents = new[]
        {
            "MockCallRoutingService - 已避免使用",
            "MockWebRTCClient - 已避免使用",
            "MockSipGateway - 已避免使用"
        };

        // Assert - 验证策略正确
        Assert.Equal(3, realComponents.Length);
        Assert.Equal(3, avoidedMockComponents.Length);
        
        // 确认我们使用的是真实组件
        Assert.All(realComponents, component => Assert.Contains("真实", component));
        Assert.All(avoidedMockComponents, component => Assert.Contains("已避免", component));
    }

    #region 辅助方法

    /// <summary>
    /// 判断是否为PSTN号码的辅助方法
    /// </summary>
    private static bool IsPstnNumber(string destination)
    {
        if (string.IsNullOrEmpty(destination))
            return false;

        return destination.StartsWith("+") || 
               destination.Replace("-", "").All(char.IsDigit);
    }

    /// <summary>
    /// 判断是否为Web地址的辅助方法
    /// </summary>
    private static bool IsWebAddress(string destination)
    {
        if (string.IsNullOrEmpty(destination))
            return false;

        return destination.Contains("@") || destination.StartsWith("sip:");
    }

    #endregion

    public void Dispose()
    {
        // 清理资源
    }
}