using AI.Caller.Core;
using AI.Caller.Core.Models;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace AI.Caller.Phone.Services {
    /// <summary>
    /// 外呼执行器实现 - 真实SIP集成
    /// </summary>
    public class OutboundCallExecutor : IOutboundCallExecutor {
        private readonly ILogger _logger;
        private readonly WebRTCSettings _webRTCSettings;
        private readonly ApplicationContext _applicationContext;
        private readonly SIPTransportManager _sipTransportManager;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly AICustomerServiceManager _aiCustomerServiceManager;
        private readonly ITtsTemplateIntegrationService _integrationService;

        public OutboundCallExecutor(
            ILogger<OutboundCallExecutor> logger,
            ApplicationContext applicationContext,
            SIPTransportManager sipTransportManager,
            IOptions<WebRTCSettings> webRTCSettings,
            IServiceScopeFactory serviceScopeFactory,
            AICustomerServiceManager aiCustomerServiceManager,
            ITtsTemplateIntegrationService integrationService
            ) {
            _logger = logger;
            _webRTCSettings = webRTCSettings.Value;
            _applicationContext = applicationContext;
            _integrationService = integrationService;
            _serviceScopeFactory = serviceScopeFactory;
            _sipTransportManager = sipTransportManager;
            _aiCustomerServiceManager = aiCustomerServiceManager;
        }

        public async Task<OutboundCallResult> ExecuteCallAsync(TtsCallRecord record, OutboundCallScript script, CancellationToken cancellationToken = default) {
            var startTime = DateTime.UtcNow;
            var callId = Guid.NewGuid().ToString();
            
            try {
                _logger.LogInformation($"开始执行外呼: {record.PhoneNumber}, CallId: {callId}");

                // 检查是否需要交互式AI客服
                var needsAI = _integrationService.ShouldEnableAICustomerService(record);
                
                if (needsAI) {
                    return await ExecuteAICallAsync(record, script, callId, startTime, cancellationToken);
                } else {
                    return await ExecuteSimpleCallAsync(record, script, callId, startTime, cancellationToken);
                }
                
            } catch (OperationCanceledException) {
                _logger.LogInformation($"外呼被取消: {record.PhoneNumber}, CallId: {callId}");
                return new OutboundCallResult {
                    Success = false,
                    Status = TtsCallStatus.Failed,
                    FailureReason = "呼叫被取消",
                    Duration = DateTime.UtcNow - startTime,
                    CallTime = startTime,
                    CallId = callId
                };
            } catch (Exception ex) {
                _logger.LogError(ex, $"外呼执行失败: {record.PhoneNumber}, CallId: {callId}");
                return new OutboundCallResult {
                    Success = false,
                    Status = TtsCallStatus.Failed,
                    FailureReason = ex.Message,
                    Duration = DateTime.UtcNow - startTime,
                    CallTime = startTime,
                    CallId = callId
                };
            }
        }

        public bool CanExecuteOutboundCall() {
            // 检查SIP传输是否可用
            if (_sipTransportManager?.SIPTransport == null) {
                _logger.LogWarning("SIP传输不可用，无法执行外呼");
                return false;
            }
            
            return true;
        }

        private async Task<OutboundCallResult> ExecuteAICallAsync(TtsCallRecord record, OutboundCallScript script, string callId, DateTime startTime, CancellationToken cancellationToken) {
            _logger.LogInformation($"执行AI交互外呼: {record.PhoneNumber}");

            try {
                // 1. 获取用户的SIP账户信息
                var user = await GetUserForOutboundCallAsync(record);
                if (user?.SipAccount == null) {
                    throw new Exception("用户SIP账户未配置");
                }

                // 2. 创建SIP客户端
                var sipClient = _applicationContext.GetSipClientByUserId(user.Id);
                if (sipClient == null) {
                    throw new Exception("创建SIP客户端失败");
                }

                try {
                    // 3. 发起SIP呼叫
                    var callResult = await InitiateSipCallAsync(sipClient, record.PhoneNumber, user, cancellationToken);
                    if (!callResult.Success) {
                        return new OutboundCallResult {
                            Success = false,
                            Status = callResult.Status,
                            FailureReason = callResult.FailureReason,
                            Duration = DateTime.UtcNow - startTime,
                            CallTime = startTime,
                            CallId = callId
                        };
                    }

                    // 4. 启动AI客服会话
                    var aiStarted = await _aiCustomerServiceManager.StartAICustomerServiceAsync(user, sipClient, script.CombinedScript);
                    if (!aiStarted) {
                        _logger.LogWarning($"AI客服启动失败，降级为简单TTS: {record.PhoneNumber}");
                        // 降级为简单TTS播放
                        return await ExecuteSimpleTtsAsync(sipClient, script, callId, startTime, cancellationToken);
                    }

                    // 5. 等待AI交互完成
                    var interactionResult = await WaitForAIInteractionAsync(user.Id, script, cancellationToken);
                    
                    return new OutboundCallResult {
                        Success = true,
                        Status = TtsCallStatus.Completed,
                        Duration = DateTime.UtcNow - startTime,
                        CallTime = startTime,
                        CallId = callId,
                        Metadata = new Dictionary<string, object> {
                            ["ai_enabled"] = true,
                            ["script_type"] = "interactive",
                            ["template_used"] = script.Template?.Name ?? "无",
                            ["script_length"] = script.CombinedScript.Length,
                            ["interaction_duration"] = interactionResult.Duration.TotalSeconds
                        }
                    };
                    
                } finally {
                    // 清理资源
                    CleanupSipClient(sipClient, user.Id);
                }
                
            } catch (Exception ex) {
                _logger.LogError(ex, $"AI外呼执行失败: {record.PhoneNumber}");
                return new OutboundCallResult {
                    Success = false,
                    Status = TtsCallStatus.Failed,
                    FailureReason = ex.Message,
                    Duration = DateTime.UtcNow - startTime,
                    CallTime = startTime,
                    CallId = callId
                };
            }
        }

        private async Task<OutboundCallResult> ExecuteSimpleCallAsync(TtsCallRecord record, OutboundCallScript script, string callId, DateTime startTime, CancellationToken cancellationToken) {
            _logger.LogInformation($"执行简单TTS外呼: {record.PhoneNumber}");

            try {
                // 1. 获取用户的SIP账户信息
                var user = await GetUserForOutboundCallAsync(record);
                if (user?.SipAccount == null) {
                    throw new Exception("用户SIP账户未配置");
                }

                // 2. 创建SIP客户端
                var sipClient = _applicationContext.GetSipClientByUserId(user.Id);
                if (sipClient == null) {
                    throw new Exception("创建SIP客户端失败");
                }

                try {
                    // 3. 发起SIP呼叫
                    var callResult = await InitiateSipCallAsync(sipClient, record.PhoneNumber, user, cancellationToken);
                    if (!callResult.Success) {
                        return new OutboundCallResult {
                            Success = false,
                            Status = callResult.Status,
                            FailureReason = callResult.FailureReason,
                            Duration = DateTime.UtcNow - startTime,
                            CallTime = startTime,
                            CallId = callId
                        };
                    }

                    // 4. 播放TTS内容
                    var ttsResult = await ExecuteSimpleTtsAsync(sipClient, script, callId, startTime, cancellationToken);
                    return ttsResult;
                    
                } finally {
                    // 清理资源
                    CleanupSipClient(sipClient, user.Id);
                }
                
            } catch (Exception ex) {
                _logger.LogError(ex, $"简单TTS外呼执行失败: {record.PhoneNumber}");
                return new OutboundCallResult {
                    Success = false,
                    Status = TtsCallStatus.Failed,
                    FailureReason = ex.Message,
                    Duration = DateTime.UtcNow - startTime,
                    CallTime = startTime,
                    CallId = callId
                };
            }
        }

        private async Task<User?> GetUserForOutboundCallAsync(TtsCallRecord record) {
            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var document = await context.TtsCallDocuments
                .FirstOrDefaultAsync(d => d.Id == record.DocumentId);
                
            if (document == null) return null;
            
            return await context.Users
                .Include(u => u.SipAccount)
                .FirstOrDefaultAsync(u => u.Id == document.UserId);
        }

        private async Task<(bool Success, TtsCallStatus Status, string? FailureReason)> InitiateSipCallAsync(SIPClient sipClient, string phoneNumber, User user, CancellationToken cancellationToken) {
            try {
                _logger.LogInformation($"发起SIP呼叫: {phoneNumber}");
                
                // 创建From头部
                var fromHeader = new SIPFromHeader(
                    user.SipAccount!.SipUsername,
                    new SIPURI(user.SipAccount.SipUsername, user.SipAccount.SipServer, null),
                    null
                );
                
                // 设置呼叫状态监听
                var callCompleted = new TaskCompletionSource<(bool Success, TtsCallStatus Status, string? FailureReason)>();
                var callAnswered = false;

                var offer = await sipClient.CreateOfferAsync();

                sipClient.MediaSessionManager!.IceCandidateGenerated += async (candidate) => {
                    if (candidate != null) {
                        _logger.LogDebug($"{candidate.toJSON()}");
                    }
                };

                sipClient.CallAnswered += (client) => {
                    callAnswered = true;
                    _logger.LogInformation($"SIP呼叫成功接通: {phoneNumber}");
                    callCompleted.TrySetResult((true, TtsCallStatus.Connected, null));
                };
                
                sipClient.CallTrying += (client) => {
                    _logger.LogDebug($"SIP呼叫尝试中: {phoneNumber}");
                };
                
                sipClient.CallEnded += (client) => {
                    if (!callAnswered) {
                        _logger.LogWarning($"SIP呼叫未接通就结束: {phoneNumber}");
                        callCompleted.TrySetResult((false, TtsCallStatus.NoAnswer, "呼叫未接通"));
                    }
                };
                
                // 发起呼叫
                await sipClient.CallAsync(phoneNumber, fromHeader);
                
                // 等待呼叫结果，最多等待30秒
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
                
                try {
                    var result = await callCompleted.Task.WaitAsync(timeoutCts.Token);
                    return result;
                } catch (OperationCanceledException) {
                    _logger.LogWarning($"SIP呼叫超时: {phoneNumber}");
                    sipClient.Cancel();
                    return (false, TtsCallStatus.NoAnswer, "呼叫超时");
                }
                
            } catch (Exception ex) {
                _logger.LogError(ex, $"SIP呼叫异常: {phoneNumber}");
                
                // 根据异常类型判断失败原因
                var failureReason = ex.Message.ToLower() switch {
                    var msg when msg.Contains("busy") => "忙线",
                    var msg when msg.Contains("timeout") => "无人接听",
                    var msg when msg.Contains("unreachable") => "号码无法接通",
                    _ => ex.Message
                };
                
                var status = failureReason switch {
                    "忙线" => TtsCallStatus.Busy,
                    "无人接听" => TtsCallStatus.NoAnswer,
                    _ => TtsCallStatus.Failed
                };
                
                return (false, status, failureReason);
            }
        }

        private async Task<OutboundCallResult> ExecuteSimpleTtsAsync(SIPClient sipClient, OutboundCallScript script, string callId, DateTime startTime, CancellationToken cancellationToken) {
            try {
                _logger.LogInformation($"开始播放TTS内容: {script.CombinedScript.Substring(0, Math.Min(50, script.CombinedScript.Length))}...");
                
                // 等待媒体连接建立
                await Task.Delay(1000, cancellationToken);
                
                // TODO: 集成真实的TTS引擎播放音频
                // 这里需要将TTS文本转换为音频并通过SIP媒体流播放
                // 目前模拟播放时间
                var estimatedDuration = Math.Max(3000, script.CombinedScript.Length * 100); // 每字符100ms
                await Task.Delay(Math.Min(estimatedDuration, 60000), cancellationToken); // 最多60秒
                
                _logger.LogInformation($"TTS播放完成");
                
                return new OutboundCallResult {
                    Success = true,
                    Status = TtsCallStatus.Completed,
                    Duration = DateTime.UtcNow - startTime,
                    CallTime = startTime,
                    CallId = callId,
                    Metadata = new Dictionary<string, object> {
                        ["ai_enabled"] = false,
                        ["script_type"] = "simple_tts",
                        ["script_length"] = script.CombinedScript.Length,
                        ["estimated_duration"] = estimatedDuration
                    }
                };
                
            } catch (Exception ex) {
                _logger.LogError(ex, "TTS播放失败");
                throw;
            }
        }

        private async Task<(TimeSpan Duration, bool Success)> WaitForAIInteractionAsync(int userId, OutboundCallScript script, CancellationToken cancellationToken) {
            var startTime = DateTime.UtcNow;
            
            try {
                // 等待AI客服会话完成，最多等待5分钟
                var maxWaitTime = TimeSpan.FromMinutes(5);
                var checkInterval = TimeSpan.FromSeconds(2);
                
                while (DateTime.UtcNow - startTime < maxWaitTime && !cancellationToken.IsCancellationRequested) {
                    // 检查AI客服是否还在运行
                    if (!_aiCustomerServiceManager.IsAICustomerServiceActive(userId)) {
                        _logger.LogInformation($"AI客服会话已结束: UserId={userId}");
                        break;
                    }
                    
                    await Task.Delay(checkInterval, cancellationToken);
                }
                
                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation($"AI交互完成，时长: {duration.TotalSeconds:F1}秒");
                
                return (duration, true);
                
            } catch (OperationCanceledException) {
                _logger.LogInformation("AI交互被取消");
                return (DateTime.UtcNow - startTime, false);
            }
        }

        private void CleanupSipClient(SIPClient? sipClient, int userId) {
            try {
                if (sipClient != null) {
                    // 停止AI客服（如果还在运行）
                    if (_aiCustomerServiceManager.IsAICustomerServiceActive(userId)) {
                        Task.Run(async () => await _aiCustomerServiceManager.StopAICustomerServiceAsync(userId));
                    }
                    
                    // 挂断呼叫
                    sipClient.Hangup();
                    
                    _logger.LogDebug($"SIP客户端资源已清理: UserId={userId}");
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"清理SIP客户端资源失败: UserId={userId}");
            }
        }
    }
}