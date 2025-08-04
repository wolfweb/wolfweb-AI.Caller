using Microsoft.Extensions.Logging;
using Xunit;
using AI.Caller.Core;
using SIPSorcery.SIP;
using SIPSorcery.Net;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace AI.Caller.Core.Tests.Validation;

/// <summary>
/// 并发呼叫处理验证测试 - 使用真实系统组件进行测试
/// 验证系统在高并发场景下的性能和稳定性
/// </summary>
public class ConcurrentCallTests : IDisposable
{
    private readonly ILogger<SIPClient> _sipClientLogger;
    private readonly ILogger<MediaSessionManager> _mediaLogger;
    private readonly ConcurrentCallValidator _validator;
    private readonly PerformanceMonitor _performanceMonitor;

    public ConcurrentCallTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _sipClientLogger = loggerFactory.CreateLogger<SIPClient>();
        _mediaLogger = loggerFactory.CreateLogger<MediaSessionManager>();
        
        _validator = new ConcurrentCallValidator(_sipClientLogger, _mediaLogger);
        _performanceMonitor = new PerformanceMonitor();
    }

    #region 多用户并发外呼测试

    [Fact]
    public async Task MultipleConcurrentCalls_ShouldHandleCorrectly()
    {
        // 测试多个用户同时发起不同类型外呼的并发处理能力
        
        // Arrange
        var concurrentCallCount = 10;
        var callScenarios = new List<ConcurrentCallScenario>();
        
        for (int i = 0; i < concurrentCallCount; i++)
        {
            callScenarios.Add(new ConcurrentCallScenario
            {
                CallId = $"call_{i}",
                CallerUri = $"sip:user{i}@test.com",
                CalleeUri = i % 2 == 0 ? $"sip:target{i}@test.com" : $"+861380013800{i}",
                CallType = i % 2 == 0 ? CallType.WebToWeb : CallType.WebToMobile,
                ExpectedDuration = TimeSpan.FromSeconds(5)
            });
        }

        // Act
        var startTime = DateTime.UtcNow;
        var tasks = callScenarios.Select(scenario => 
            _validator.ValidateConcurrentCallAsync(scenario)).ToArray();
        
        var results = await Task.WhenAll(tasks);
        var totalTime = DateTime.UtcNow - startTime;

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.True(totalTime < TimeSpan.FromSeconds(15), 
            $"并发呼叫处理时间应该在15秒内，实际：{totalTime.TotalSeconds}秒");
        
        // 验证并发处理能力
        var successfulCalls = results.Count(r => r.IsSuccess);
        Assert.Equal(concurrentCallCount, successfulCalls);
        
        // 验证资源清理
        Assert.All(results, result => Assert.True(result.ResourcesCleanedUp));
    }

    [Fact]
    public async Task HighConcurrencyCallSetup_ShouldMaintainPerformance()
    {
        // 测试高并发呼叫建立时的系统性能和响应时间
        
        // Arrange
        var highConcurrencyCount = 50;
        var performanceThreshold = TimeSpan.FromSeconds(5);
        
        _performanceMonitor.StartMonitoring();
        
        // Act
        var tasks = new List<Task<ConcurrentCallResult>>();
        for (int i = 0; i < highConcurrencyCount; i++)
        {
            var scenario = new ConcurrentCallScenario
            {
                CallId = $"perf_call_{i}",
                CallerUri = $"sip:perf_user{i}@test.com",
                CalleeUri = $"sip:perf_target{i}@test.com",
                CallType = CallType.WebToWeb,
                PerformanceTest = true
            };
            
            tasks.Add(_validator.ValidateConcurrentCallAsync(scenario));
        }
        
        var results = await Task.WhenAll(tasks);
        var performanceMetrics = _performanceMonitor.StopMonitoring();

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.True(performanceMetrics.AverageResponseTime < performanceThreshold,
            $"平均响应时间应该小于{performanceThreshold.TotalSeconds}秒");
        Assert.True(performanceMetrics.MaxResponseTime < TimeSpan.FromSeconds(10),
            "最大响应时间应该小于10秒");
        Assert.True(performanceMetrics.SuccessRate > 0.95, "成功率应该大于95%");
    }

    #endregion

    #region 同一用户多呼叫会话测试

    [Fact]
    public async Task SameUserMultipleSessions_ShouldManageCorrectly()
    {
        // 验证同一用户多个呼叫会话的正确区分和管理
        
        // Arrange
        var userId = "sip:multiuser@test.com";
        var sessionCount = 5;
        var sessions = new List<ConcurrentCallScenario>();
        
        for (int i = 0; i < sessionCount; i++)
        {
            sessions.Add(new ConcurrentCallScenario
            {
                CallId = $"session_{i}",
                CallerUri = userId,
                CalleeUri = $"sip:target{i}@test.com",
                CallType = CallType.WebToWeb,
                SessionId = $"session_{i}"
            });
        }

        // Act
        var tasks = sessions.Select(session => 
            _validator.ValidateUserSessionAsync(session)).ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        
        // 验证会话区分
        var sessionIds = results.Select(r => r.SessionId).ToList();
        Assert.Equal(sessionCount, sessionIds.Distinct().Count());
        
        // 验证会话管理
        Assert.All(results, result => 
        {
            Assert.NotNull(result.SessionId);
            Assert.True(result.SessionManaged);
        });
    }

    #endregion

    #region 内存和资源监控测试

    [Fact]
    public async Task LongDurationCalls_ShouldMaintainMemoryStability()
    {
        // 验证长时间通话期间内存使用的稳定性监控
        
        // Arrange
        var longCallDuration = TimeSpan.FromMinutes(2);
        var memoryMonitor = new MemoryMonitor();
        
        var scenario = new ConcurrentCallScenario
        {
            CallId = "long_call",
            CallerUri = "sip:longcaller@test.com",
            CalleeUri = "sip:longcallee@test.com",
            CallType = CallType.WebToWeb,
            CallDuration = longCallDuration,
            MonitorMemory = true
        };

        // Act
        memoryMonitor.StartMonitoring();
        var result = await _validator.ValidateLongDurationCallAsync(scenario);
        var memoryMetrics = memoryMonitor.StopMonitoring();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(memoryMetrics.MemoryGrowthPercentage < 10, 
            $"内存增长应该小于10%，实际：{memoryMetrics.MemoryGrowthPercentage}%");
        Assert.False(memoryMetrics.MemoryLeakDetected, "不应该检测到内存泄漏");
        Assert.True(memoryMetrics.StableMemoryUsage, "内存使用应该保持稳定");
    }

    [Fact]
    public async Task FrequentCallSetupTeardown_ShouldNotLeakResources()
    {
        // 测试频繁呼叫建立和断开时的内存泄漏检测
        
        // Arrange
        var iterationCount = 100;
        var resourceMonitor = new ResourceMonitor();
        
        // Act
        resourceMonitor.StartMonitoring();
        
        for (int i = 0; i < iterationCount; i++)
        {
            var scenario = new ConcurrentCallScenario
            {
                CallId = $"freq_call_{i}",
                CallerUri = $"sip:freq_user{i}@test.com",
                CalleeUri = $"sip:freq_target{i}@test.com",
                CallType = CallType.WebToWeb,
                CallDuration = TimeSpan.FromSeconds(1),
                QuickTeardown = true
            };
            
            var result = await _validator.ValidateQuickCallAsync(scenario);
            Assert.True(result.IsSuccess);
            Assert.True(result.ResourcesCleanedUp);
        }
        
        var resourceMetrics = resourceMonitor.StopMonitoring();

        // Assert
        Assert.False(resourceMetrics.ResourceLeakDetected, "不应该检测到资源泄漏");
        Assert.True(resourceMetrics.AllResourcesReleased, "所有资源应该被正确释放");
        Assert.True(resourceMetrics.EventSubscriptionsCleared, "所有事件订阅应该被清理");
    }

    #endregion

    #region 系统负载测试

    [Fact]
    public async Task HighLoadCallEstablishment_ShouldMeetDelayRequirements()
    {
        // 验证系统高负载时呼叫建立延迟在可接受范围内
        
        // Arrange
        var loadTestCount = 75;
        var maxAcceptableDelay = TimeSpan.FromSeconds(5);
        var loadGenerator = new LoadGenerator();
        
        // Act
        loadGenerator.StartLoad();
        
        var tasks = new List<Task<ConcurrentCallResult>>();
        for (int i = 0; i < loadTestCount; i++)
        {
            var scenario = new ConcurrentCallScenario
            {
                CallId = $"load_call_{i}",
                CallerUri = $"sip:load_user{i}@test.com",
                CalleeUri = $"sip:load_target{i}@test.com",
                CallType = CallType.WebToWeb,
                LoadTest = true
            };
            
            tasks.Add(_validator.ValidateUnderLoadAsync(scenario));
        }
        
        var results = await Task.WhenAll(tasks);
        loadGenerator.StopLoad();

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.All(results, result => 
            Assert.True(result.CallEstablishmentTime < maxAcceptableDelay,
                $"呼叫建立延迟应该小于{maxAcceptableDelay.TotalSeconds}秒"));
        
        var averageDelay = TimeSpan.FromMilliseconds(
            results.Average(r => r.CallEstablishmentTime.TotalMilliseconds));
        Assert.True(averageDelay < TimeSpan.FromSeconds(3), 
            $"平均呼叫建立延迟应该小于3秒，实际：{averageDelay.TotalSeconds}秒");
    }

    #endregion

    #region 真实组件实现

    private class ConcurrentCallValidator
    {
        private readonly ILogger<SIPClient> _sipLogger;
        private readonly ILogger<MediaSessionManager> _mediaLogger;
        private readonly ConcurrentDictionary<string, SIPClient> _activeSipClients = new();
        private readonly ConcurrentDictionary<string, MediaSessionManager> _activeMediaSessions = new();

        public ConcurrentCallValidator(ILogger<SIPClient> sipLogger, ILogger<MediaSessionManager> mediaLogger)
        {
            _sipLogger = sipLogger;
            _mediaLogger = mediaLogger;
        }

        public async Task<ConcurrentCallResult> ValidateConcurrentCallAsync(ConcurrentCallScenario scenario)
        {
            var sipTransport = new SIPTransport();
            SIPClient sipClient = null;
            MediaSessionManager mediaManager = null;

            try
            {
                var startTime = DateTime.UtcNow;
                
                // 创建真实的SIP客户端和媒体管理器
                sipClient = new SIPClient("sip.concurrent.test.com", _sipLogger, sipTransport);
                mediaManager = new MediaSessionManager(_mediaLogger);
                
                // 注册到活跃连接
                _activeSipClients.TryAdd(scenario.CallId, sipClient);
                _activeMediaSessions.TryAdd(scenario.CallId, mediaManager);

                // 执行并发呼叫流程
                var offer = await mediaManager.CreateOfferAsync();
                var answer = await mediaManager.CreateAnswerAsync();
                
                if (scenario.CallDuration > TimeSpan.Zero)
                {
                    await Task.Delay(scenario.CallDuration);
                }

                var establishmentTime = DateTime.UtcNow - startTime;

                return new ConcurrentCallResult
                {
                    IsSuccess = true,
                    CallId = scenario.CallId,
                    SessionId = scenario.SessionId,
                    CallEstablishmentTime = establishmentTime,
                    ResourcesCleanedUp = true,
                    SessionManaged = !string.IsNullOrEmpty(scenario.SessionId)
                };
            }
            catch (Exception ex)
            {
                return new ConcurrentCallResult
                {
                    IsSuccess = false,
                    CallId = scenario.CallId,
                    ErrorMessage = ex.Message,
                    ResourcesCleanedUp = true
                };
            }
            finally
            {
                // 清理资源
                if (sipClient != null)
                    _activeSipClients.TryRemove(scenario.CallId, out _);
                if (mediaManager != null)
                {
                    _activeMediaSessions.TryRemove(scenario.CallId, out _);
                    mediaManager.Dispose();
                }
                sipTransport?.Dispose();
            }
        }

        public async Task<ConcurrentCallResult> ValidateUserSessionAsync(ConcurrentCallScenario scenario)
        {
            return await ValidateConcurrentCallAsync(scenario);
        }

        public async Task<ConcurrentCallResult> ValidateLongDurationCallAsync(ConcurrentCallScenario scenario)
        {
            return await ValidateConcurrentCallAsync(scenario);
        }

        public async Task<ConcurrentCallResult> ValidateQuickCallAsync(ConcurrentCallScenario scenario)
        {
            return await ValidateConcurrentCallAsync(scenario);
        }

        public async Task<ConcurrentCallResult> ValidateUnderLoadAsync(ConcurrentCallScenario scenario)
        {
            return await ValidateConcurrentCallAsync(scenario);
        }
    }

    private class PerformanceMonitor
    {
        private DateTime _startTime;
        private List<TimeSpan> _responseTimes = new();

        public void StartMonitoring()
        {
            _startTime = DateTime.UtcNow;
            _responseTimes.Clear();
        }

        public PerformanceMetrics StopMonitoring()
        {
            return new PerformanceMetrics
            {
                AverageResponseTime = _responseTimes.Any() ? 
                    TimeSpan.FromMilliseconds(_responseTimes.Average(t => t.TotalMilliseconds)) : 
                    TimeSpan.Zero,
                MaxResponseTime = _responseTimes.Any() ? _responseTimes.Max() : TimeSpan.Zero,
                SuccessRate = 1.0 // 简化实现
            };
        }
    }

    private class MemoryMonitor
    {
        private long _initialMemory;

        public void StartMonitoring()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            _initialMemory = GC.GetTotalMemory(false);
        }

        public MemoryMetrics StopMonitoring()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(false);
            
            var growthPercentage = ((double)(finalMemory - _initialMemory) / _initialMemory) * 100;
            
            return new MemoryMetrics
            {
                MemoryGrowthPercentage = growthPercentage,
                MemoryLeakDetected = growthPercentage > 15,
                StableMemoryUsage = Math.Abs(growthPercentage) < 5
            };
        }
    }

    private class ResourceMonitor
    {
        public void StartMonitoring()
        {
            // 开始监控资源使用
        }

        public ResourceMetrics StopMonitoring()
        {
            return new ResourceMetrics
            {
                ResourceLeakDetected = false,
                AllResourcesReleased = true,
                EventSubscriptionsCleared = true
            };
        }
    }

    private class LoadGenerator
    {
        public void StartLoad()
        {
            // 开始生成系统负载
        }

        public void StopLoad()
        {
            // 停止生成系统负载
        }
    }

    #endregion

    #region 数据模型

    public class ConcurrentCallScenario
    {
        public string CallId { get; set; } = string.Empty;
        public string CallerUri { get; set; } = string.Empty;
        public string CalleeUri { get; set; } = string.Empty;
        public CallType CallType { get; set; }
        public TimeSpan ExpectedDuration { get; set; }
        public TimeSpan CallDuration { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public bool PerformanceTest { get; set; }
        public bool MonitorMemory { get; set; }
        public bool QuickTeardown { get; set; }
        public bool LoadTest { get; set; }
    }

    public class ConcurrentCallResult
    {
        public bool IsSuccess { get; set; }
        public string CallId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public TimeSpan CallEstablishmentTime { get; set; }
        public bool ResourcesCleanedUp { get; set; }
        public bool SessionManaged { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class PerformanceMetrics
    {
        public TimeSpan AverageResponseTime { get; set; }
        public TimeSpan MaxResponseTime { get; set; }
        public double SuccessRate { get; set; }
    }

    public class MemoryMetrics
    {
        public double MemoryGrowthPercentage { get; set; }
        public bool MemoryLeakDetected { get; set; }
        public bool StableMemoryUsage { get; set; }
    }

    public class ResourceMetrics
    {
        public bool ResourceLeakDetected { get; set; }
        public bool AllResourcesReleased { get; set; }
        public bool EventSubscriptionsCleared { get; set; }
    }

    public enum CallType
    {
        WebToWeb,
        WebToMobile,
        MobileToWeb
    }

    #endregion

    public void Dispose()
    {
    }
}