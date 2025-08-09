using AI.Caller.Core;
using AI.Caller.Core.Models;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
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

        public SipService(
            ILogger<SipService> logger,
            AppDbContext dbContext,
            IHubContext<WebRtcHub> hubContext,
            ApplicationContext applicationContext,
            SIPTransportManager sipTransportManager,
            IOptions<WebRTCSettings> webRTCSettings,
            IServiceScopeFactory serviceScopeProvider,
            HangupMonitoringService? monitoringService = null
        ) {
            _logger = logger;
            _dbContext = dbContext;
            _hubContext = hubContext;
            _webRTCSettings = webRTCSettings.Value;
            _applicationContext = applicationContext;
            _sipTransportManager = sipTransportManager;
            _retryPolicy = new HangupRetryPolicy();
            _monitoringService = monitoringService ?? new HangupMonitoringService(LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HangupMonitoringService>());
            _serviceScopeProvider = serviceScopeProvider;
        }

        /// <summary>
        /// 为用户注册SIP账号
        /// </summary>
        public async Task<bool> RegisterUserAsync(User user) {
            if (string.IsNullOrEmpty(user.SipUsername)) {
                _logger.LogWarning($"用户 {user.Username} 的SIP账号信息不完整");
                return false;
            }

            try {
                if (user.RegisteredAt == null || user.RegisteredAt < DateTime.UtcNow.AddHours(-2) || user.RegisteredAt < _applicationContext.StartAt) {
                    RegisterAsync(user);
                    user.SipRegistered = true; 
                    user.RegisteredAt = DateTime.UtcNow;
                }

                var sipClient = new SIPClient(_applicationContext.SipServer,_logger, _sipTransportManager.SIPTransport!, _webRTCSettings);

                sipClient.StatusMessage += (_, message) => {
                    _logger.LogDebug($"SIP客户端状态更新: {message}");
                };

                sipClient.CallAnswered += async _ => {
                    await _hubContext.Clients.User(user.Id.ToString()).SendAsync("answered");
                };

                sipClient.CallFinishedWithContext += async (client, context) => {
                    await HandleCallFinishedWithContext(user.SipUsername, context);
                };

                _applicationContext.AddSipClient(user.SipUsername, sipClient);
                _logger.LogInformation($"用户 {user.Username} : {user.SipUsername} 的SIP账号注册成功");
                await _dbContext.SaveChangesAsync();
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"用户 {user.Username} 的SIP账号注册失败");
                return false;
            }
        }

        /// <summary>
        /// 发起呼叫
        /// </summary>
        public async Task<(bool Success, string Message)> MakeCallAsync(string destination, string sipUsername, RTCSessionDescriptionInit sdpOffer) {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == sipUsername);
            try {
                if (!_applicationContext.SipClients.TryGetValue(sipUsername, out var sipClient)) {
                    if (user == null || !user.SipRegistered) {
                        return (false, "用户未注册SIP账号或SIP账号未激活");
                    }

                    var registered = await RegisterUserAsync(user);
                    if (!registered || !_applicationContext.SipClients.TryGetValue(sipUsername, out sipClient)) {
                        return (false, "无法获取SIP客户端");
                    }
                }

                var offer = await sipClient.OfferAsync(sdpOffer);
                await _hubContext.Clients.User(user.Id.ToString()).SendAsync("sdpAnswered", offer.toJSON());

                sipClient.MediaSessionManager!.IceCandidateGenerated += async (candidate) => {
                    if (candidate != null) {
                        try
                        {
                            await _hubContext.Clients.User(user.Id.ToString()).SendAsync("receiveIceCandidate", candidate.toJSON());
                        }catch(Exception e)
                        {
                            _logger.LogError(e, e.Message);
                        }
                    }
                };

                var fromTag = GenerateFromTag();
                
                var fromHeader = new SIPFromHeader(user.Username, new SIPURI(user.SipUsername, _applicationContext.SipServer, null), fromTag);
                
                await sipClient.CallAsync(destination, fromHeader);
                
                RegisterOutboundCallAfterInitiation(sipClient, fromTag, sipUsername, destination);
                
                _applicationContext.UpdateUserActivity(sipUsername);

                return (true, "呼叫已发起");
            } catch (Exception ex) {
                _logger.LogError(ex, $"发起呼叫失败: {destination}");
                await _hubContext.Clients.User(user.Id.ToString()).SendAsync("callTimeout");
                return (false, $"呼叫失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 接听电话
        /// </summary>
        /// <returns></returns>
        public async Task<bool> AnswerAsync(string userName, RTCSessionDescriptionInit answerSdp) {
            var user = _dbContext.Users.First(x => x.Username == userName);
            if (_applicationContext.SipClients.TryGetValue(user.SipUsername, out var sipClient)) {
                try {
                    sipClient.SetRemoteDescription(answerSdp);

                    while (!sipClient.IsSecureContextReady())
                        await Task.Delay(100);

                    var result = await sipClient.AnswerAsync();
                    _logger.LogInformation($"用户 {userName} 接听电话{(result ? "成功" : "失败")}");
                    
                    if (result) {
                        _applicationContext.UpdateUserActivity(user.SipUsername);
                    }
                    
                    return result;
                } catch (Exception ex) {
                    _logger.LogError(ex, $"用户 {userName} 接听电话失败: {ex.Message}");
                }
            } else {
                _logger.LogWarning($"用户 {userName} 的SIP客户端不存在，无法接听电话");
            }
            return false;
        }

        /// <summary>
        /// 简化的挂断电话方法
        /// </summary>
        public async Task<bool> HangupCallAsync(string sipUserName, string? reason = null) {
            var hangupReason = reason ?? "User requested hangup";
            
            try {
                _logger.LogInformation($"开始挂断电话 - 用户: {sipUserName}, 原因: {hangupReason}");

                if (!_applicationContext.SipClients.TryGetValue(sipUserName, out var sipClient)) {
                    _logger.LogWarning($"用户 {sipUserName} 的SIP客户端不存在，无法挂断");
                    await NotifyHangupStatusAsync(sipUserName, "hangupFailed", "SIP客户端不存在");
                    return false;
                }

                if (!sipClient.IsCallActive) {
                    _logger.LogInformation($"用户 {sipUserName} 没有活动的通话，无需挂断");
                    await NotifyHangupStatusAsync(sipUserName, "callEnded", "没有活动通话");
                    return true;
                }

                _logger.LogInformation($"用户 {sipUserName} 有活动通话，执行挂断");

                sipClient.Hangup();
                
                await Task.Delay(500);
                
                await NotifyHangupStatusAsync(sipUserName, "callEnded", hangupReason);
                
                _applicationContext.UpdateUserActivity(sipUserName);
                
                _logger.LogInformation($"用户 {sipUserName} 挂断电话成功");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"用户 {sipUserName} 挂断电话失败");
                await NotifyHangupStatusAsync(sipUserName, "hangupFailed", $"挂断失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 强制终止连接
        /// </summary>
        private async Task ForceTerminateConnectionAsync(string sipUserName, SIPClient sipClient) {
            try {
                _logger.LogInformation($"强制终止用户 {sipUserName} 的连接");

                // 强制停止音频流
                sipClient.StopAudioStreams();

                // 强制释放媒体资源
                sipClient.ReleaseMediaResources();

                // 尝试发送BYE消息（但不等待响应）
                try {
                    using var forceTerminateCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await Task.Run(() => sipClient.Hangup(), forceTerminateCts.Token);
                } catch (Exception ex) {
                    _logger.LogWarning(ex, $"强制终止时发送BYE消息失败，但继续清理资源");
                }

                _logger.LogInformation($"用户 {sipUserName} 的连接已强制终止");
            } catch (Exception ex) {
                _logger.LogError(ex, $"强制终止用户 {sipUserName} 连接时发生错误");
            }
        }

        /// <summary>
        /// 通知挂断状态（带超时处理）
        /// </summary>
        private async Task NotifyHangupStatusAsync(string sipUserName, string status, string message) {
            using var notificationCts = new CancellationTokenSource(_retryPolicy.NotificationTimeout);

            try {
                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == sipUserName, notificationCts.Token);
                if (user != null) {
                    var notificationTask = _hubContext.Clients.User(user.Id.ToString())
                        .SendAsync(status, new {
                            message = message,
                            timestamp = DateTime.UtcNow,
                            sipUsername = sipUserName
                        }, notificationCts.Token);

                    // 等待通知发送完成或超时
                    var completedTask = await Task.WhenAny(
                        notificationTask,
                        Task.Delay(_retryPolicy.NotificationTimeout, notificationCts.Token)
                    );

                    if (completedTask == notificationTask) {
                        await notificationTask; // 确保任何异常被抛出
                        _logger.LogInformation($"已向用户 {sipUserName} 发送状态通知: {status} - {message}");
                    } else {
                        _logger.LogWarning($"向用户 {sipUserName} 发送状态通知超时: {status} - {message}");
                    }
                } else {
                    _logger.LogWarning($"未找到SIP用户名为 {sipUserName} 的用户，无法发送状态通知");
                }
            } catch (OperationCanceledException) when (notificationCts.Token.IsCancellationRequested) {
                _logger.LogWarning($"向用户 {sipUserName} 发送状态通知被取消或超时: {status}");
            } catch (Exception ex) {
                _logger.LogError(ex, $"发送挂断状态通知失败 - 用户: {sipUserName}, 状态: {status}");
            }
        }

        /// <summary>
        /// 添加ICE候选者
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="candidate"></param>
        public void AddIceCandidate(string userName, RTCIceCandidateInit candidate) {
            var user = _dbContext.Users.First(x => x.Username == userName);
            if (_applicationContext.SipClients.TryGetValue(user.SipUsername, out var sipClient)) {
                sipClient.AddIceCandidate(candidate);
            }
        }

        /// <summary>
        /// 发送DTMF音调
        /// </summary>
        public async Task<bool> SendDtmfAsync(string sipUserName, byte tone) {
            try {
                if (_applicationContext.SipClients.TryGetValue(sipUserName, out var sipClient)) {
                    await sipClient.SendDTMFAsync(tone);
                    return true;
                }
                return false;
            } catch (Exception ex) {
                _logger.LogError(ex, "发送DTMF失败");
                return false;
            }
        }

        public bool GetSecureContextReady(string sipUserName) {
            try {
                if (_applicationContext.SipClients.TryGetValue(sipUserName, out var sipClient)) {
                    return sipClient.IsSecureContextReady() == true;
                }
                return false;
            } catch (Exception ex) {
                return false;
            }
        }

        /// <summary>
        /// 带通知的挂断电话
        /// </summary>
        public async Task<bool> HangupWithNotificationAsync(string sipUserName, string? reason = null) {
            var callId = Guid.NewGuid().ToString();
            var hangupNotification = new HangupNotification {
                CallId = callId,
                InitiatorSipUsername = sipUserName,
                Reason = reason ?? "User initiated hangup",
                Status = HangupStatus.Initiated,
                Timestamp = DateTime.UtcNow
            };

            try {
                _logger.LogInformation($"开始挂断通话 - 用户: {sipUserName}, 通话ID: {callId}, 原因: {hangupNotification.Reason}");

                if (!_applicationContext.SipClients.TryGetValue(sipUserName, out var sipClient)) {
                    _logger.LogWarning($"用户 {sipUserName} 的SIP客户端不存在，无法挂断");
                    return false;
                }

                if (!sipClient.IsCallActive) {
                    _logger.LogInformation($"用户 {sipUserName} 没有活动的通话，无需挂断");
                    return true;
                }

                // 获取对方用户信息用于通知
                string? targetSipUsername = null;
                if (sipClient.Dialogue != null) {
                    // 从SIP对话中提取对方的用户名
                    var remoteUri = sipClient.Dialogue.RemoteUserField?.URI?.User;
                    if (!string.IsNullOrEmpty(remoteUri)) {
                        targetSipUsername = remoteUri;
                        hangupNotification.TargetSipUsername = targetSipUsername;
                    }
                }

                // 执行挂断操作
                var hangupSuccess = await HangupCallAsync(sipUserName, reason);

                if (hangupSuccess) {
                    hangupNotification.Status = HangupStatus.Completed;
                    _logger.LogInformation($"用户 {sipUserName} 挂断成功");

                    // 通知对方用户
                    if (!string.IsNullOrEmpty(targetSipUsername)) {
                        await NotifyRemotePartyHangupAsync(targetSipUsername, hangupNotification.Reason);
                        hangupNotification.Status = HangupStatus.NotificationSent;
                    }

                    return true;
                } else {
                    hangupNotification.Status = HangupStatus.Failed;
                    _logger.LogError($"用户 {sipUserName} 挂断失败");
                    return false;
                }
            } catch (Exception ex) {
                hangupNotification.Status = HangupStatus.Failed;
                _logger.LogError(ex, $"挂断通话时发生异常 - 用户: {sipUserName}, 通话ID: {callId}");
                return false;
            }
        }

        /// <summary>
        /// 通知对方用户挂断事件
        /// </summary>
        public async Task<bool> NotifyRemotePartyHangupAsync(string targetSipUsername, string reason) {
            var retryCount = 0;
            var delay = _retryPolicy.RetryDelay;

            while (retryCount < _retryPolicy.MaxRetries) {
                try {
                    _logger.LogInformation($"尝试通知用户 {targetSipUsername} 挂断事件 (第 {retryCount + 1} 次尝试)");

                    // 查找目标用户
                    var targetUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == targetSipUsername);
                    if (targetUser == null) {
                        _logger.LogWarning($"未找到SIP用户名为 {targetSipUsername} 的用户");
                        return false;
                    }

                    // 创建通话结束事件
                    var callEndedEvent = new CallEndedEvent {
                        CallId = Guid.NewGuid().ToString(),
                        SipUsername = targetSipUsername,
                        EndTime = DateTime.UtcNow,
                        EndReason = reason,
                        AudioStopped = true,
                        ResourcesReleased = true,
                        RemoteNotified = true
                    };

                    // 使用SignalR通知前端
                    using var cts = new CancellationTokenSource(_retryPolicy.NotificationTimeout);
                    await _hubContext.Clients.User(targetUser.Id.ToString())
                        .SendAsync("callEnded", new {
                            reason = reason,
                            timestamp = callEndedEvent.EndTime,
                            callId = callEndedEvent.CallId
                        }, cts.Token);

                    _logger.LogInformation($"成功通知用户 {targetSipUsername} 挂断事件");
                    return true;
                } catch (OperationCanceledException) {
                    _logger.LogWarning($"通知用户 {targetSipUsername} 超时 (第 {retryCount + 1} 次尝试)");
                } catch (Exception ex) {
                    _logger.LogError(ex, $"通知用户 {targetSipUsername} 挂断事件失败 (第 {retryCount + 1} 次尝试)");
                }

                retryCount++;
                if (retryCount < _retryPolicy.MaxRetries) {
                    _logger.LogInformation($"等待 {delay.TotalSeconds} 秒后重试通知用户 {targetSipUsername}");
                    await Task.Delay(delay);

                    // 指数退避策略
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, _retryPolicy.MaxRetryDelay.TotalMilliseconds));
                }
            }

            _logger.LogError($"通知用户 {targetSipUsername} 挂断事件失败，已达到最大重试次数 {_retryPolicy.MaxRetries}");
            return false;
        }

        /// <summary>
        /// 生成From标签
        /// </summary>
        private string GenerateFromTag()
        {
            // 生成符合SIP标准的From-tag
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var random = new Random().Next(1000, 9999);
            return $"tag-{timestamp}-{random}";
        }

        /// <summary>
        /// 在呼叫发起后注册呼出通话
        /// </summary>
        private void RegisterOutboundCallAfterInitiation(SIPClient sipClient, string fromTag, string sipUsername, string destination)
        {
            try
            {
                // 创建一个一次性的事件处理器来监听CallInitiated事件
                Action<SIPClient, string>? callInitiatedHandler = null;
                callInitiatedHandler = (client, callId) =>
                {
                    try
                    {
                        // 注册呼出通话到CallTypeIdentifier
                        var callTypeIdentifier = GetCallTypeIdentifier();
                        if (callTypeIdentifier != null)
                        {
                            callTypeIdentifier.RegisterOutboundCallWithSipTags(callId, fromTag, sipUsername, destination);
                            _logger.LogInformation($"已注册呼出通话 - CallId: {callId}, FromTag: {fromTag}, SipUsername: {sipUsername}, Destination: {destination}");
                        }
                        
                        // 移除事件处理器，避免重复注册
                        if (callInitiatedHandler != null)
                        {
                            client.CallInitiated -= callInitiatedHandler;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"处理CallInitiated事件时发生错误 - SipUsername: {sipUsername}");
                        // 移除事件处理器
                        if (callInitiatedHandler != null)
                        {
                            client.CallInitiated -= callInitiatedHandler;
                        }
                    }
                };
                
                // 添加事件处理器
                sipClient.CallInitiated += callInitiatedHandler;
                
                // 设置超时清理，防止事件处理器泄漏
                _ = Task.Run(async () =>
                {
                    await Task.Delay(30000); // 30秒超时
                    try
                    {
                        if (callInitiatedHandler != null)
                        {
                            sipClient.CallInitiated -= callInitiatedHandler;
                            _logger.LogDebug($"已清理呼出通话注册事件处理器 - SipUsername: {sipUsername}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"清理事件处理器时发生错误 - SipUsername: {sipUsername}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"设置呼出通话注册事件处理器时发生错误 - SipUsername: {sipUsername}");
            }
        }

        private ICallTypeIdentifier? GetCallTypeIdentifier()
        {
            using var scope = _serviceScopeProvider.CreateScope();
            try
            {
                return scope.ServiceProvider.GetService<ICallTypeIdentifier>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取CallTypeIdentifier服务失败");
                return null;
            }
        }

        private void RegisterAsync(User user) {
            //var tcs = new TaskCompletionSource<bool>();
            var sipRegistrationClient = new SIPRegistrationUserAgent(_sipTransportManager.SIPTransport, user.SipUsername, user.SipPassword, _applicationContext.SipServer, 180);

            sipRegistrationClient.RegistrationSuccessful += (uri, resp) => {
                _logger.LogDebug($"register success for {uri} => {resp}");
                //tcs.TrySetResult(true);
            };

            sipRegistrationClient.RegistrationFailed += (uri, resp, err) => {
                _logger.LogError($"register failed for {uri} => {resp}, {err}");
                //tcs.TrySetResult(false);
            };

            sipRegistrationClient.Start();

            //return await tcs.Task;
        }

        private async Task HandleCallFinishedWithContext(string sipUsername, HangupEventContext context) {
            try {
                if (context.IsRemoteInitiated) {
                    _logger.LogInformation($"检测到远程挂断，通知Web端用户: {sipUsername}");
                    await NotifyWebClientRemoteHangup(sipUsername, context.Reason);
                } else {
                    _logger.LogInformation($"本地发起的挂断，无需通知Web端: {sipUsername}");
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理挂断事件失败 - 用户: {sipUsername}");
            }
        }

        private async Task NotifyWebClientRemoteHangup(string sipUsername, string reason) {
            using var scope = _serviceScopeProvider.CreateScope();
            var _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == sipUsername);
            if (user == null) {
                _logger.LogWarning($"未找到SIP用户名为 {sipUsername} 的用户");
                return;
            }

            var retryCount = 0;
            var delay = _retryPolicy.RetryDelay;

            while (retryCount < _retryPolicy.MaxRetries) {
                try {
                    using var cts = new CancellationTokenSource(_retryPolicy.NotificationTimeout);
                    await _hubContext.Clients.User(user.Id.ToString())
                        .SendAsync("remoteHangup", new { reason }, cts.Token);

                    _logger.LogInformation($"成功通知Web端用户 {sipUsername} 远程挂断事件");
                    return;
                } catch (OperationCanceledException) {
                    _logger.LogWarning($"通知Web端用户 {sipUsername} 超时 (第 {retryCount + 1} 次尝试)");
                } catch (Exception ex) {
                    _logger.LogError(ex, $"通知Web端用户 {sipUsername} 远程挂断事件失败 (第 {retryCount + 1} 次尝试)");
                }

                retryCount++;
                if (retryCount < _retryPolicy.MaxRetries) {
                    await Task.Delay(delay);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, _retryPolicy.MaxRetryDelay.TotalMilliseconds));
                }
            }

            _logger.LogError($"通知Web端用户 {sipUsername} 远程挂断事件失败，已达到最大重试次数 {_retryPolicy.MaxRetries}");
        }
    }
}