using AI.Caller.Core;
using AI.Caller.Core.Models;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using System;

namespace AI.Caller.Phone.Services {
    public class SipService {
        private readonly ILogger _logger;
        private readonly AppDbContext _dbContext;
        private readonly HangupRetryPolicy _retryPolicy;
        private readonly WebRTCSettings _webRTCSettings;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly ApplicationContext _applicationContext;
        private readonly SIPTransportManager _sipTransportManager;
        private readonly HangupMonitoringService _monitoringService;

        private readonly IServiceScopeFactory _serviceScopeProvider;
        private readonly AICustomerServiceManager _aiCustomerServiceManager;
        private readonly AICustomerServiceSettings _aiSettings;

        public SipService(
            ILogger<SipService> logger,
            AppDbContext dbContext,
            IHubContext<WebRtcHub> hubContext,
            ApplicationContext applicationContext,
            SIPTransportManager sipTransportManager,
            IOptions<WebRTCSettings> webRTCSettings,
            IServiceScopeFactory serviceScopeProvider,
            AICustomerServiceManager aiCustomerServiceManager,
            IOptions<AICustomerServiceSettings> aiSettings,
            HangupMonitoringService? monitoringService = null
        ) {
            _logger = logger;
            _dbContext = dbContext;
            _hubContext = hubContext;
            _aiSettings = aiSettings.Value;
            _webRTCSettings = webRTCSettings.Value;
            _applicationContext = applicationContext;
            _sipTransportManager = sipTransportManager;
            _retryPolicy = new HangupRetryPolicy();
            _monitoringService = monitoringService ?? new HangupMonitoringService(LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HangupMonitoringService>());
            _serviceScopeProvider = serviceScopeProvider;
            _aiCustomerServiceManager = aiCustomerServiceManager;
        }

        public async Task<bool> RegisterUserAsync(User user) {
            if (user.SipAccount == null || string.IsNullOrEmpty(user.SipAccount.SipUsername)) {
                _logger.LogWarning($"用户 {user.Username} 的SIP账号信息不完整");
                return false;
            }

            try {
                if (user.RegisteredAt == null || user.RegisteredAt < DateTime.UtcNow.AddHours(-2) || user.RegisteredAt < _applicationContext.StartAt) {
                    RegisterAsync(user);
                    user.SipRegistered = true;
                    user.RegisteredAt = DateTime.UtcNow;
                }

                var sipClient = new SIPClient(user.SipAccount.SipServer, _logger, _sipTransportManager.SIPTransport!, _webRTCSettings);

                sipClient.StatusMessage += (_, message) => {
                    _logger.LogDebug($"SIP客户端状态更新: {message}");
                };

                sipClient.CallAnswered += async _ => {
                    await _hubContext.Clients.User(user.Id.ToString()).SendAsync("answered");
                    
                    // 检查是否启用呼出后自动启动AI
                    if (_aiSettings.Enabled && _aiSettings.AutoStartOnOutbound) {
                        Task.Run(async () => {
                            try {
                                await Task.Delay(1000); // 等待1秒确保通话稳定
                                await StartAICustomerServiceAsync(user, _aiSettings.DefaultWelcomeScript);
                            } catch (Exception ex) {
                                _logger.LogError(ex, $"呼出后自动启动AI客服失败 - 用户: {user.Username}");
                            }
                        });
                    }
                };

                sipClient.CallFinishedWithContext += async (client, context) => {
                    await HandleCallFinishedWithContext(user.Id, context);
                };

                _applicationContext.AddSipClient(user.Id, sipClient);
                _logger.LogDebug($"用户 {user.Username} : {user.SipAccount.SipUsername} 的SIP账号注册成功");
                await _dbContext.SaveChangesAsync();
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"用户 {user.Username} 的SIP账号注册失败");
                return false;
            }
        }

        public async Task UnregisterUserAsync(User user) {
            if (user != null) {
                _applicationContext.RemoveSipClientByUserId(user.Id);
                user.SipRegistered = false;
                user.RegisteredAt = null;
                await _dbContext.SaveChangesAsync();
            }
        }

        public async Task<(bool Success, string Message)> MakeCallAsync(string destination, User user, RTCSessionDescriptionInit sdpOffer) {            
            try {
                if (!_applicationContext.SipClients.TryGetValue(user.Id, out var sipClient)) {
                    if (user == null || !user.SipRegistered) {
                        return (false, "用户未注册SIP账号或SIP账号未激活");
                    }

                    var registered = await RegisterUserAsync(user);
                    if (!registered || !_applicationContext.SipClients.TryGetValue(user.Id, out sipClient)) {
                        return (false, "无法获取SIP客户端");
                    }
                }

                var offer = await sipClient.OfferAsync(sdpOffer);
                await _hubContext.Clients.User(user.Id.ToString()).SendAsync("sdpAnswered", offer.toJSON());

                sipClient.MediaSessionManager!.IceCandidateGenerated += async (candidate) => {
                    if (candidate != null) {
                        try {
                            await _hubContext.Clients.User(user.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                        } catch (Exception e) {
                            _logger.LogError(e, e.Message);
                        }
                    }
                };

                var fromTag = GenerateFromTag(user);

                var fromHeader = new SIPFromHeader(user.Username, new SIPURI(user.SipAccount!.SipUsername, user.SipAccount.SipServer, $"id={fromTag}"), fromTag);

                await sipClient.CallAsync(destination, fromHeader);

                RegisterOutboundCallAfterInitiation(sipClient, fromTag, user.SipAccount!.SipUsername, destination);

                _applicationContext.UpdateUserActivityByUserId(user.Id);

                return (true, "呼叫已发起");
            } catch (Exception ex) {
                _logger.LogError(ex, $"发起呼叫失败: {destination}");
                await _hubContext.Clients.User(user.Id.ToString()).SendAsync("callTimeout");
                return (false, $"呼叫失败: {ex.Message}");
            }
        }

        public async Task<bool> AnswerAsync(User user, RTCSessionDescriptionInit answerSdp) {            
            if (_applicationContext.SipClients.TryGetValue(user.Id, out var sipClient)) {
                try {
                    sipClient.SetRemoteDescription(answerSdp);

                    var timeout = TimeSpan.FromSeconds(10);
                    var start = DateTime.UtcNow;
                    while (!sipClient.IsSecureContextReady()) {
                        if (DateTime.UtcNow - start > timeout) {
                            _logger.LogError($"*** SECURE CONTEXT TIMEOUT *** User: {user.Username}, waited {timeout.TotalSeconds}s");
                            return false;
                        }
                        await Task.Delay(100);
                    }

                    var result = await sipClient.AnswerAsync();

                    if (!result) {
                        _logger.LogError($"*** ANSWER FAILED *** User: {user.Username}, SipUsername: {user.SipAccount?.SipUsername}");
                    }

                    if (result) {
                        _applicationContext.UpdateUserActivityByUserId(user.Id);
                        
                        // 检查是否启用来电后自动启动AI
                        if (_aiSettings.Enabled && _aiSettings.AutoStartOnInbound) {
                            _ = Task.Run(async () => {
                                try {
                                    await Task.Delay(1000); // 等待1秒确保通话稳定
                                    await StartAICustomerServiceAsync(user, _aiSettings.DefaultWelcomeScript);
                                } catch (Exception ex) {
                                    _logger.LogError(ex, $"来电接听后自动启动AI客服失败 - 用户: {user.Username}");
                                }
                            });
                        }
                    }

                    return result;
                } catch (Exception ex) {
                    _logger.LogError(ex, $"用户 {user.Username} 接听电话失败: {ex.Message}");
                }
            } else {
                _logger.LogWarning($"用户 {user.Username} 的SIP客户端不存在，无法接听电话");
            }
            return false;
        }

        public async Task<bool> HangupCallAsync(User user, string? reason = null) {
            var hangupReason = reason ?? "User requested hangup";

            try {
                _logger.LogDebug($"开始挂断电话 - 用户: {user.Username}, 原因: {hangupReason}");

                if (!_applicationContext.SipClients.TryGetValue(user.Id, out var sipClient)) {
                    _logger.LogWarning($"用户 {user.Username} 的SIP客户端不存在，无法挂断");
                    await NotifyHangupStatusAsync("hangupFailed", "SIP客户端不存在", user);
                    return false;
                }

                if (!sipClient.IsCallActive) {
                    _logger.LogDebug($"用户 {user.Username} 没有活动的通话，无需挂断");
                    await NotifyHangupStatusAsync("callEnded", "没有活动通话", user);
                    return true;
                }

                _logger.LogDebug($"用户 {user.Username} 有活动通话，执行挂断");

                // 如果有活跃的AI客服会话，先停止它
                if (_aiCustomerServiceManager.IsAICustomerServiceActive(user.Id)) {
                    try {
                        await _aiCustomerServiceManager.StopAICustomerServiceAsync(user.Id);
                        _logger.LogDebug($"已停止用户 {user.Username} 的AI客服会话");
                    } catch (Exception ex) {
                        _logger.LogError(ex, $"停止AI客服会话失败 - 用户: {user.Username}");
                    }
                }

                sipClient.Hangup();

                await Task.Delay(500);

                await NotifyHangupStatusAsync("callEnded", hangupReason, user);

                _applicationContext.UpdateUserActivityByUserId(user.Id);

                _logger.LogDebug($"用户 {user.Username} 挂断电话成功");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"用户 {user.Username} 挂断电话失败");
                await NotifyHangupStatusAsync("hangupFailed", $"挂断失败: {ex.Message}", user);
                return false;
            }
        }

        private async Task ForceTerminateConnectionAsync(User user, SIPClient sipClient) {
            try {
                _logger.LogDebug($"强制终止用户 {user.Id} 的连接");

                sipClient.StopAudioStreams();

                sipClient.ReleaseMediaResources();

                try {
                    using var forceTerminateCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await Task.Run(() => sipClient.Hangup(), forceTerminateCts.Token);
                } catch (Exception ex) {
                    _logger.LogWarning(ex, $"强制终止时发送BYE消息失败，但继续清理资源");
                }

                _logger.LogDebug($"用户 {user.Username} 的连接已强制终止");
            } catch (Exception ex) {
                _logger.LogError(ex, $"强制终止用户 {user.Username} 连接时发生错误");
            }
        }

        private async Task NotifyHangupStatusAsync(string status, string message, User user) {
            using var notificationCts = new CancellationTokenSource(_retryPolicy.NotificationTimeout);

            try {
                var notificationTask = _hubContext.Clients.User(user.Id.ToString())
                    .SendAsync(status, new {
                        message = message,
                        timestamp = DateTime.UtcNow,
                        sipUsername = user.SipAccount!.SipUsername
                    }, notificationCts.Token);

                var completedTask = await Task.WhenAny(
                    notificationTask,
                    Task.Delay(_retryPolicy.NotificationTimeout, notificationCts.Token)
                );

                if (completedTask == notificationTask) {
                    await notificationTask; // 确保任何异常被抛出
                    _logger.LogDebug($"已向用户 {user.Username} 发送状态通知: {status} - {message}");
                } else {
                    _logger.LogWarning($"向用户 {user.Username} 发送状态通知超时: {status} - {message}");
                }
            } catch (OperationCanceledException) when (notificationCts.Token.IsCancellationRequested) {
                _logger.LogWarning($"向用户 {user.Username} 发送状态通知被取消或超时: {status}");
            } catch (Exception ex) {
                _logger.LogError(ex, $"发送挂断状态通知失败 - 用户: {user.Username}, 状态: {status}");
            }
        }

        public void AddIceCandidate(User user, RTCIceCandidateInit candidate) {
            if (_applicationContext.SipClients.TryGetValue(user.Id, out var sipClient)) {
                sipClient.AddIceCandidate(candidate);
            }
        }

        public async Task<bool> SendDtmfAsync(byte tone, User user) {
            try {
                if (_applicationContext.SipClients.TryGetValue(user.Id, out var sipClient)) {
                    await sipClient.SendDTMFAsync(tone);
                    return true;
                }
                return false;
            } catch (Exception ex) {
                _logger.LogError(ex, "发送DTMF失败");
                return false;
            }
        }

        public bool GetSecureContextReady(User user) {
            try {                
                if (_applicationContext.SipClients.TryGetValue(user.Id, out var sipClient)) {
                    return sipClient.IsSecureContextReady() == true;
                }
                return false;
            } catch (Exception ex) {
                return false;
            }
        }

        public async Task<bool> HangupWithNotificationAsync(User currentUser, WebRtcHangupModel model) {
            var callId = Guid.NewGuid().ToString();
            
            if (model.CallContext?.CallId != null) {
                callId = model.CallContext.CallId;
            }
            
            var hangupNotification = new HangupNotification {
                CallId = callId,
                Reason = model.Reason ?? "User initiated hangup",
                Status = HangupStatus.Initiated,
                Timestamp = DateTime.UtcNow,
                InitiatorSipUsername = currentUser.SipAccount!.SipUsername,
            };

            try {
                _logger.LogDebug($"开始挂断通话 - 用户: {currentUser.Username}, 通话ID: {callId}, 原因: {hangupNotification.Reason}");

                if (model.CallContext == null) {
                    _logger.LogError("挂断操作缺少必需的调用上下文信息 - 用户: {Username}, Target: {Target}", 
                        currentUser.Username, model.Target);
                    
                    await _hubContext.Clients.User(currentUser.Id.ToString()).SendAsync("hangupFailed", new { 
                        message = "挂断失败：缺少必需的通话上下文信息", 
                        timestamp = DateTime.UtcNow 
                    });
                    
                    return false;
                }

                _logger.LogInformation("Hangup context - Caller: {Caller}, Callee: {Callee}, IsExternal: {IsExternal}", model.CallContext.Caller?.SipUsername, model.CallContext.Callee?.SipUsername, model.CallContext.IsExternal);
                
                return await HandleContextBasedHangupAsync(currentUser, model, hangupNotification);
            } catch (Exception ex) {
                hangupNotification.Status = HangupStatus.Failed;
                _logger.LogError(ex, $"挂断通话时发生异常 - 用户: {currentUser.Username}, 通话ID: {callId}");
                
                await _hubContext.Clients.User(currentUser.Id.ToString()).SendAsync("hangupFailed", new { 
                    message = $"挂断过程中发生错误: {ex.Message}", 
                    timestamp = DateTime.UtcNow 
                });
                
                return false;
            }
        }

        private async Task<bool> HandleContextBasedHangupAsync(User currentUser, WebRtcHangupModel model, HangupNotification hangupNotification) {
            var callContext = model.CallContext;
            
            if (!_applicationContext.SipClients.TryGetValue(currentUser.Id, out var currentUserSipClient)) {
                _logger.LogWarning($"用户 {currentUser.Username} 的SIP客户端不存在，无法挂断");
                return false;
            }

            if (callContext!.IsExternal) {                
                if (!string.IsNullOrEmpty(callContext.Callee?.UserId) && int.TryParse(callContext.Callee.UserId, out int calleeUserId)) {
                    if (_aiCustomerServiceManager.IsAICustomerServiceActive(calleeUserId)) {
                        try {
                            await _aiCustomerServiceManager.StopAICustomerServiceAsync(calleeUserId);
                        } catch (Exception ex) {
                            _logger.LogError(ex, $"停止目标用户AI客服失败 - 用户ID: {calleeUserId}");
                        }
                    }
                    
                    if (_applicationContext.SipClients.TryGetValue(calleeUserId, out var calleeSipClient)) {
                        calleeSipClient.Hangup();
                        _logger.LogInformation($"成功挂断外部呼叫 - 当前用户: {currentUser.Username}, 目标用户ID: {calleeUserId}");
                    } else {
                        currentUserSipClient.Cancel();
                        _logger.LogInformation($"目标用户SIP客户端不存在，取消当前用户呼叫 - 当前用户: {currentUser.Username}");
                    }
                } else {
                    currentUserSipClient.Cancel();
                    _logger.LogInformation($"无法识别目标用户，取消当前用户呼叫 - 当前用户: {currentUser.Username}");
                }
                
                if (_aiCustomerServiceManager.IsAICustomerServiceActive(currentUser.Id)) {
                    try {
                        await _aiCustomerServiceManager.StopAICustomerServiceAsync(currentUser.Id);
                    } catch (Exception ex) {
                        _logger.LogError(ex, $"停止当前用户AI客服失败 - 用户: {currentUser.Username}");
                    }
                }
            } else {
                User? targetUser = null;
                
                if (!string.IsNullOrEmpty(callContext.Caller?.UserId) && !string.IsNullOrEmpty(callContext.Callee?.UserId)) {                    
                    if (int.TryParse(callContext.Caller.UserId, out int callerUserId) && int.TryParse(callContext.Callee.UserId, out int calleeUserId)) {
                        int targetUserId = currentUser.Id == callerUserId ? calleeUserId : callerUserId;                        
                        targetUser = await _dbContext.Users.Include(u => u.SipAccount).FirstOrDefaultAsync(u => u.Id == targetUserId);
                            
                        if (targetUser != null) {
                            if (_aiCustomerServiceManager.IsAICustomerServiceActive(targetUserId)) {
                                try {
                                    await _aiCustomerServiceManager.StopAICustomerServiceAsync(targetUserId);
                                } catch (Exception ex) {
                                    _logger.LogError(ex, $"停止目标用户AI客服失败 - 用户: {targetUser.Username}");
                                }
                            }
                            
                            if (_applicationContext.SipClients.TryGetValue(targetUserId, out var targetSipClient)) {
                                targetSipClient.Hangup();
                                await _hubContext.Clients.User(targetUser.Id.ToString()).SendAsync("callEnded", new {
                                    reason = hangupNotification.Reason,
                                    timestamp = DateTime.UtcNow,
                                    callContext = callContext
                                });
                                _logger.LogInformation($"成功挂断Web到Web通话 - 当前用户: {currentUser.Username}, 目标用户: {targetUser.Username}");
                            } else {
                                currentUserSipClient.Hangup();
                                _logger.LogInformation($"目标用户SIP客户端不存在，挂断当前用户 - 当前用户: {currentUser.Username}");
                            }
                        } else {
                            currentUserSipClient.Cancel();
                            _logger.LogInformation($"目标用户不存在，取消当前用户呼叫 - 当前用户: {currentUser.Username}");
                        }
                    } else {
                        currentUserSipClient.Cancel();
                        _logger.LogWarning($"无法解析用户ID，使用默认挂断逻辑 - 当前用户: {currentUser.Username}");
                    }
                } else {
                    currentUserSipClient.Cancel();
                    _logger.LogWarning($"缺少完整的呼叫者/被叫者信息，使用默认挂断逻辑 - 当前用户: {currentUser.Username}");
                }
                
                if (_aiCustomerServiceManager.IsAICustomerServiceActive(currentUser.Id)) {
                    try {
                        await _aiCustomerServiceManager.StopAICustomerServiceAsync(currentUser.Id);
                    } catch (Exception ex) {
                        _logger.LogError(ex, $"停止当前用户AI客服失败 - 用户: {currentUser.Username}");
                    }
                }
            }

            return true;
        }

        public async Task<bool> NotifyRemotePartyHangupAsync(string reason, User targetUser, HangupCallContext? callContext = null) {
            var retryCount = 0;
            var delay = _retryPolicy.RetryDelay;

            while (retryCount < _retryPolicy.MaxRetries) {
                try {
                    _logger.LogInformation($"尝试通知用户 {targetUser.Username} 挂断事件 (第 {retryCount + 1} 次尝试)");

                    var callEndedEvent = new CallEndedEvent {
                        CallId = Guid.NewGuid().ToString(),
                        SipUsername = targetUser.SipAccount!.SipUsername,
                        EndTime = DateTime.UtcNow,
                        EndReason = reason,
                        AudioStopped = true,
                        ResourcesReleased = true,
                        RemoteNotified = true
                    };

                    using var cts = new CancellationTokenSource(_retryPolicy.NotificationTimeout);
                    await _hubContext.Clients.User(targetUser.Id.ToString())
                        .SendAsync("callEnded", new {
                            reason = reason,
                            timestamp = callEndedEvent.EndTime,
                            callId = callEndedEvent.CallId,
                            callContext = callContext
                        }, cts.Token);

                    _logger.LogInformation($"成功通知用户 {targetUser.Username} 挂断事件");
                    return true;
                } catch (OperationCanceledException) {
                    _logger.LogWarning($"通知用户 {targetUser.Username} 超时 (第 {retryCount + 1} 次尝试)");
                } catch (Exception ex) {
                    _logger.LogError(ex, $"通知用户 {targetUser.Username} 挂断事件失败 (第 {retryCount + 1} 次尝试)");
                }

                retryCount++;
                if (retryCount < _retryPolicy.MaxRetries) {
                    _logger.LogInformation($"等待 {delay.TotalSeconds} 秒后重试通知用户 {targetUser.Username}");
                    await Task.Delay(delay);

                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, _retryPolicy.MaxRetryDelay.TotalMilliseconds));
                }
            }

            _logger.LogError($"无法通知用户 {targetUser.Username} 挂断事件，已达到最大重试次数 ({_retryPolicy.MaxRetries})");
            return false;
        }

        private string GenerateFromTag(User user) {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var random = new Random().Next(1000, 9999);
            return $"tag-{timestamp}-{user.Id}-{random}";
        }

        private void RegisterOutboundCallAfterInitiation(SIPClient sipClient, string fromTag, string sipUsername, string destination) {
            try {
                Action<SIPClient, string>? callInitiatedHandler = null;
                callInitiatedHandler = (client, callId) => {
                    try {
                        var callTypeIdentifier = GetCallTypeIdentifier();
                        if (callTypeIdentifier != null) {
                            callTypeIdentifier.RegisterOutboundCallWithSipTags(callId, fromTag, sipUsername, destination);
                            _logger.LogInformation($"已注册呼出通话 - CallId: {callId}, FromTag: {fromTag}, SipUsername: {sipUsername}, Destination: {destination}");
                        }

                        if (callInitiatedHandler != null) {
                            client.CallInitiated -= callInitiatedHandler;
                        }
                    } catch (Exception ex) {
                        _logger.LogError(ex, $"处理CallInitiated事件时发生错误 - SipUsername: {sipUsername}");
                        if (callInitiatedHandler != null) {
                            client.CallInitiated -= callInitiatedHandler;
                        }
                    }
                };

                sipClient.CallInitiated += callInitiatedHandler;

                _ = Task.Run(async () => {
                    await Task.Delay(30000);
                    try {
                        if (callInitiatedHandler != null) {
                            sipClient.CallInitiated -= callInitiatedHandler;
                            _logger.LogInformation($"已清理呼出通话注册事件处理器 - SipUsername: {sipUsername}");
                        }
                    } catch (Exception ex) {
                        _logger.LogError(ex, $"清理事件处理器时发生错误 - SipUsername: {sipUsername}");
                    }
                });
            } catch (Exception ex) {
                _logger.LogError(ex, $"设置呼出通话注册事件处理器时发生错误 - SipUsername: {sipUsername}");
            }
        }

        private ICallTypeIdentifier? GetCallTypeIdentifier() {
            using var scope = _serviceScopeProvider.CreateScope();
            try {
                return scope.ServiceProvider.GetService<ICallTypeIdentifier>();
            } catch (Exception ex) {
                _logger.LogError(ex, "获取CallTypeIdentifier服务失败");
                return null;
            }
        }

        private void RegisterAsync(User user) {
            //var tcs = new TaskCompletionSource<bool>();
            if (user.SipAccount?.SipUsername == null || user.SipAccount?.SipPassword == null) {
                throw new InvalidOperationException("用户SIP账号信息不完整");
            }

            var sipRegistrationClient = new SIPRegistrationUserAgent(_sipTransportManager.SIPTransport, user.SipAccount.SipUsername, user.SipAccount.SipPassword, user.SipAccount.SipServer, 180);

            sipRegistrationClient.RegistrationSuccessful += (uri, resp) => {
                _logger.LogInformation($"register success for {uri} => {resp}");
                //tcs.TrySetResult(true);
            };

            sipRegistrationClient.RegistrationFailed += (uri, resp, err) => {
                _logger.LogError($"register failed for {uri} => {resp}, {err}");
                //tcs.TrySetResult(false);
            };

            sipRegistrationClient.Start();

            //return await tcs.Task;
        }

        private async Task HandleCallFinishedWithContext(int userId, HangupEventContext context) {
            try {
                if (context.IsRemoteInitiated) {
                    _logger.LogInformation($"检测到远程挂断，通知Web端用户: {userId}");
                    await NotifyWebClientRemoteHangup(userId, context.Reason);
                } else {
                    _logger.LogInformation($"本地发起的挂断，无需通知Web端: {userId}");
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理挂断事件失败 - 用户: {userId}");
            }
        }

        private async Task NotifyWebClientRemoteHangup(int userId, string reason) {
            using var scope = _serviceScopeProvider.CreateScope();
            var _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await _dbContext.Users.Include(x => x.SipAccount).FirstOrDefaultAsync(x=>x.Id == userId);
            if (user == null) {
                _logger.LogWarning($"未找到SIP用户名为 {userId} 的用户");
                return;
            }

            var retryCount = 0;
            var delay = _retryPolicy.RetryDelay;

            while (retryCount < _retryPolicy.MaxRetries) {
                try {
                    using var cts = new CancellationTokenSource(_retryPolicy.NotificationTimeout);
                    await _hubContext.Clients.User(user.Id.ToString())
                        .SendAsync("remoteHangup", new { reason }, cts.Token);

                    _logger.LogInformation($"成功通知Web端用户 {user.Id}->{user.SipAccount?.SipUsername} 远程挂断事件");
                    return;
                } catch (OperationCanceledException) {
                    _logger.LogWarning($"通知Web端用户 {user.Id}->{user.SipAccount?.SipUsername} 超时 (第 {retryCount + 1} 次尝试)");
                } catch (Exception ex) {
                    _logger.LogError(ex, $"通知Web端用户 {user.Id}->{user.SipAccount?.SipUsername} 远程挂断事件失败 (第 {retryCount + 1} 次尝试)");
                }

                retryCount++;
                if (retryCount < _retryPolicy.MaxRetries) {
                    await Task.Delay(delay);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, _retryPolicy.MaxRetryDelay.TotalMilliseconds));
                }
            }

            _logger.LogError($"通知Web端用户 {user.Id}->{user.SipAccount?.SipUsername} 远程挂断事件失败，已达到最大重试次数 {_retryPolicy.MaxRetries}");
        }

        public async Task<bool> StartAICustomerServiceAsync(User user, string scriptText) {
            try {
                if (!_applicationContext.SipClients.TryGetValue(user.Id, out var sipClient)) {
                    _logger.LogWarning($"用户 {user.Username} 的SIP客户端不存在，无法启动AI客服");
                    return false;
                }

                if (!sipClient.IsCallActive) {
                    _logger.LogWarning($"用户 {user.Username} 没有活跃通话，无法启动AI客服");
                    return false;
                }

                var result = await _aiCustomerServiceManager.StartAICustomerServiceAsync(user, sipClient, scriptText);
                if (result) {
                    _logger.LogInformation($"AI客服已为用户 {user.Username} 启动");
                    
                    await _hubContext.Clients.User(user.Id.ToString()).SendAsync("aiCustomerServiceStarted", new {
                        message = "AI客服已启动",
                        scriptText = scriptText,
                        timestamp = DateTime.UtcNow
                    });
                }

                return result;
            } catch (Exception ex) {
                _logger.LogError(ex, $"启动AI客服失败 - 用户: {user.Username}");
                return false;
            }
        }

        public async Task<bool> StopAICustomerServiceAsync(User user) {
            try {
                var result = await _aiCustomerServiceManager.StopAICustomerServiceAsync(user.Id);
                if (result) {
                    _logger.LogInformation($"AI客服已为用户 {user.Username} 停止");
                    
                    await _hubContext.Clients.User(user.Id.ToString()).SendAsync("aiCustomerServiceStopped", new {
                        message = "AI客服已停止",
                        timestamp = DateTime.UtcNow
                    });
                }

                return result;
            } catch (Exception ex) {
                _logger.LogError(ex, $"停止AI客服失败 - 用户: {user.Username}");
                return false;
            }
        }

        public bool IsAICustomerServiceActive(int userId) {
            return _aiCustomerServiceManager.IsAICustomerServiceActive(userId);
        }

        public async Task PerformMaintenanceAsync() {
            var expiredClientIds = _applicationContext.GetInactiveUserIds(TimeSpan.FromHours(2));
            foreach (var clientId in expiredClientIds) {
                if (_applicationContext.SipClients.TryRemove(clientId, out var sipClient)) {
                    try {
                        await _aiCustomerServiceManager.StopAICustomerServiceAsync(clientId);
                        
                        sipClient.Hangup();
                        _logger.LogInformation($"已清理过期的SIP客户端: {clientId}");
                    } catch (Exception ex) {
                        _logger.LogError(ex, $"清理SIP客户端 {clientId} 时发生错误");
                    }
                }
            }
            await Task.CompletedTask;
        }
    }
}