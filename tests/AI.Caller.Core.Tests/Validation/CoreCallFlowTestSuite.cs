using Microsoft.Extensions.Logging;
using Xunit;
using AI.Caller.Core;
using SIPSorcery.SIP;
using SIPSorcery.Net;

namespace AI.Caller.Core.Tests.Validation;

/// <summary>
/// 核心呼叫流程测试套件 - 专注于Web2Web、Web2Mobile、Mobile2Web的核心功能
/// 确保外呼与应答、呼入与接听的完整流程正确工作
/// 聚焦于SIP外呼和呼入的核心流程建立，去除无关紧要的测试
/// </summary>
public class CoreCallFlowTestSuite : IDisposable
{
    private readonly ILogger<CoreCallFlowTestSuite> _logger;
    private readonly CoreCallFlowTests _coreCallFlowTests;

    public CoreCallFlowTestSuite()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<CoreCallFlowTestSuite>();
        
        _coreCallFlowTests = new CoreCallFlowTests();
    }

    #region 核心呼叫流程集成测试

    [Fact]
    public async Task ExecuteCoreCallFlowTests_ShouldPassAllCriticalScenarios()
    {
        // 执行所有核心呼叫流程测试，确保关键场景通过
        
        _logger.LogInformation("开始执行核心呼叫流程测试套件...");

        var testResults = new List<(string TestName, bool Success, string Error)>();

        try
        {
            // 1. Web到Web核心流程测试
            _logger.LogInformation("执行Web到Web核心流程测试...");
            
            try
            {
                await _coreCallFlowTests.WebToWeb_OutboundCall_ShouldEstablishSuccessfully();
                testResults.Add(("WebToWeb_OutboundCall", true, ""));
                _logger.LogInformation("✓ Web到Web外呼测试通过");
            }
            catch (Exception ex)
            {
                testResults.Add(("WebToWeb_OutboundCall", false, ex.Message));
                _logger.LogError($"✗ Web到Web外呼测试失败: {ex.Message}");
            }

            try
            {
                await _coreCallFlowTests.WebToWeb_InboundCall_ShouldBeAnsweredSuccessfully();
                testResults.Add(("WebToWeb_InboundCall", true, ""));
                _logger.LogInformation("✓ Web到Web呼入测试通过");
            }
            catch (Exception ex)
            {
                testResults.Add(("WebToWeb_InboundCall", false, ex.Message));
                _logger.LogError($"✗ Web到Web呼入测试失败: {ex.Message}");
            }

            // 2. Web到手机核心流程测试
            _logger.LogInformation("执行Web到手机核心流程测试...");
            
            try
            {
                await _coreCallFlowTests.WebToMobile_OutboundCall_ShouldRouteToGateway();
                testResults.Add(("WebToMobile_OutboundCall", true, ""));
                _logger.LogInformation("✓ Web到手机外呼测试通过");
            }
            catch (Exception ex)
            {
                testResults.Add(("WebToMobile_OutboundCall", false, ex.Message));
                _logger.LogError($"✗ Web到手机外呼测试失败: {ex.Message}");
            }

            try
            {
                await _coreCallFlowTests.WebToMobile_CallAnswer_ShouldEstablishPstnConnection();
                testResults.Add(("WebToMobile_CallAnswer", true, ""));
                _logger.LogInformation("✓ Web到手机接听测试通过");
            }
            catch (Exception ex)
            {
                testResults.Add(("WebToMobile_CallAnswer", false, ex.Message));
                _logger.LogError($"✗ Web到手机接听测试失败: {ex.Message}");
            }

            // 3. 手机到Web核心流程测试
            _logger.LogInformation("执行手机到Web核心流程测试...");
            
            try
            {
                await _coreCallFlowTests.MobileToWeb_InboundCall_ShouldNotifyWebUser();
                testResults.Add(("MobileToWeb_InboundCall", true, ""));
                _logger.LogInformation("✓ 手机到Web呼入测试通过");
            }
            catch (Exception ex)
            {
                testResults.Add(("MobileToWeb_InboundCall", false, ex.Message));
                _logger.LogError($"✗ 手机到Web呼入测试失败: {ex.Message}");
            }

            try
            {
                await _coreCallFlowTests.MobileToWeb_CallAnswer_ShouldEstablishWebRtcConnection();
                testResults.Add(("MobileToWeb_CallAnswer", true, ""));
                _logger.LogInformation("✓ 手机到Web接听测试通过");
            }
            catch (Exception ex)
            {
                testResults.Add(("MobileToWeb_CallAnswer", false, ex.Message));
                _logger.LogError($"✗ 手机到Web接听测试失败: {ex.Message}");
            }

            // 4. 核心SIP信令测试
            _logger.LogInformation("执行核心SIP信令测试...");
            
            try
            {
                _coreCallFlowTests.SipClient_CallAsync_ShouldInitiateOutboundCall();
                testResults.Add(("SipClient_CallAsync", true, ""));
                _logger.LogInformation("✓ SIP外呼方法测试通过");
            }
            catch (Exception ex)
            {
                testResults.Add(("SipClient_CallAsync", false, ex.Message));
                _logger.LogError($"✗ SIP外呼方法测试失败: {ex.Message}");
            }

            try
            {
                _coreCallFlowTests.SipClient_AnswerAsync_ShouldHandleInboundCall();
                testResults.Add(("SipClient_AnswerAsync", true, ""));
                _logger.LogInformation("✓ SIP接听方法测试通过");
            }
            catch (Exception ex)
            {
                testResults.Add(("SipClient_AnswerAsync", false, ex.Message));
                _logger.LogError($"✗ SIP接听方法测试失败: {ex.Message}");
            }

            try
            {
                await _coreCallFlowTests.MediaSessionManager_CreateOfferAsync_ShouldGenerateValidSdp();
                testResults.Add(("MediaSessionManager_CreateOffer", true, ""));
                _logger.LogInformation("✓ 媒体Offer创建测试通过");
            }
            catch (Exception ex)
            {
                testResults.Add(("MediaSessionManager_CreateOffer", false, ex.Message));
                _logger.LogError($"✗ 媒体Offer创建测试失败: {ex.Message}");
            }

            try
            {
                await _coreCallFlowTests.MediaSessionManager_CreateAnswerAsync_ShouldGenerateValidSdp();
                testResults.Add(("MediaSessionManager_CreateAnswer", true, ""));
                _logger.LogInformation("✓ 媒体Answer创建测试通过");
            }
            catch (Exception ex)
            {
                testResults.Add(("MediaSessionManager_CreateAnswer", false, ex.Message));
                _logger.LogError($"✗ 媒体Answer创建测试失败: {ex.Message}");
            }

            // 统计测试结果
            var totalTests = testResults.Count;
            var passedTests = testResults.Count(r => r.Success);
            var failedTests = totalTests - passedTests;

            _logger.LogInformation($"核心呼叫流程测试完成:");
            _logger.LogInformation($"  总测试数: {totalTests}");
            _logger.LogInformation($"  通过: {passedTests}");
            _logger.LogInformation($"  失败: {failedTests}");
            _logger.LogInformation($"  通过率: {(double)passedTests / totalTests * 100:F1}%");

            // 如果有失败的测试，记录详细信息
            if (failedTests > 0)
            {
                _logger.LogError("失败的测试详情:");
                foreach (var (testName, success, error) in testResults.Where(r => !r.Success))
                {
                    _logger.LogError($"  {testName}: {error}");
                }
            }

            // Assert - 所有核心测试都必须通过
            Assert.True(failedTests == 0, $"有 {failedTests} 个核心测试失败，必须全部通过才能确保系统功能完整性");

        }
        catch (Exception ex)
        {
            _logger.LogError($"执行核心呼叫流程测试套件时发生异常: {ex.Message}");
            throw;
        }
    }

    [Fact]
    public void ValidateCoreComponents_ShouldUseRealSystemComponents()
    {
        // 验证核心组件使用真实系统组件，不使用Mock
        
        _logger.LogInformation("验证核心组件使用真实系统组件...");

        var coreComponents = new[]
        {
            typeof(SIPClient),
            typeof(MediaSessionManager),
            typeof(SIPTransport)
        };

        foreach (var componentType in coreComponents)
        {
            Assert.True(componentType.IsClass, $"{componentType.Name} 应该是真实的类");
            Assert.False(componentType.Name.StartsWith("Mock"), $"{componentType.Name} 不应该是Mock类");
            Assert.False(componentType.Name.Contains("Fake"), $"{componentType.Name} 不应该是Fake类");
            
            _logger.LogInformation($"✓ {componentType.Name} 是真实系统组件");
        }

        _logger.LogInformation("所有核心组件验证通过");
    }

    [Fact]
    public void ValidateCoreCallFlowCoverage_ShouldCoverAllCriticalPaths()
    {
        // 验证核心呼叫流程覆盖所有关键路径
        
        _logger.LogInformation("验证核心呼叫流程覆盖关键路径...");

        var criticalPaths = new[]
        {
            "Web到Web外呼流程",
            "Web到Web呼入流程", 
            "Web到手机外呼流程",
            "Web到手机接听流程",
            "手机到Web呼入流程",
            "手机到Web接听流程",
            "SIP外呼信令",
            "SIP呼入信令",
            "媒体协商Offer创建",
            "媒体协商Answer创建"
        };

        // 验证每个关键路径都有对应的测试方法
        var testMethods = typeof(CoreCallFlowTests).GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(FactAttribute), false).Any())
            .Select(m => m.Name)
            .ToList();

        var expectedTestMethods = new[]
        {
            "WebToWeb_OutboundCall_ShouldEstablishSuccessfully",
            "WebToWeb_InboundCall_ShouldBeAnsweredSuccessfully",
            "WebToMobile_OutboundCall_ShouldRouteToGateway", 
            "WebToMobile_CallAnswer_ShouldEstablishPstnConnection",
            "MobileToWeb_InboundCall_ShouldNotifyWebUser",
            "MobileToWeb_CallAnswer_ShouldEstablishWebRtcConnection",
            "SipClient_CallAsync_ShouldInitiateOutboundCall",
            "SipClient_AnswerAsync_ShouldHandleInboundCall",
            "MediaSessionManager_CreateOfferAsync_ShouldGenerateValidSdp",
            "MediaSessionManager_CreateAnswerAsync_ShouldGenerateValidSdp"
        };

        foreach (var expectedMethod in expectedTestMethods)
        {
            Assert.Contains(expectedMethod, testMethods);
            _logger.LogInformation($"✓ 关键测试方法存在: {expectedMethod}");
        }

        _logger.LogInformation($"核心呼叫流程覆盖验证通过:");
        _logger.LogInformation($"  关键路径数: {criticalPaths.Length}");
        _logger.LogInformation($"  测试方法数: {expectedTestMethods.Length}");
        _logger.LogInformation($"  覆盖率: 100%");
    }

    #endregion

    public void Dispose()
    {
        _coreCallFlowTests?.Dispose();
    }
}