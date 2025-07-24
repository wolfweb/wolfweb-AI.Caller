using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Asn1.Ocsp;
using SIPSorcery.Net;
using SIPSorcery.SIP;

namespace AI.Caller.Phone.Services {
    public class SipService {
        private readonly ILogger _logger;
        private readonly AppDbContext _dbContext;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly ApplicationContext _applicationContext;
        private readonly SIPTransportManager _sipTransportManager;
        private readonly HangupRetryPolicy _retryPolicy;
        private readonly HangupMonitoringService _monitoringService;
        private readonly WebRTCSettings _webRTCSettings;

        public SipService(
            ILogger<SipService> logger,
            AppDbContext dbContext,
            IHubContext<WebRtcHub> hubContext,
            ApplicationContext applicationContext,
            SIPTransportManager sipTransportManager,
            IOptions<WebRTCSettings> webRTCSettings,
            HangupMonitoringService? monitoringService = null
        ) {
            _logger = logger;
            _dbContext = dbContext;
            _hubContext = hubContext;
            _applicationContext = applicationContext;
            _sipTransportManager = sipTransportManager;
            _webRTCSettings = webRTCSettings.Value;
            _retryPolicy = new HangupRetryPolicy(); // 使用默认配置
            _monitoringService = monitoringService ?? new HangupMonitoringService(
                Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole())
                    .CreateLogger<HangupMonitoringService>());
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
                // 创建SIP客户端选项
                var sipOptions = new SIPClientOptions {
                    SIPUsername = user.SipUsername,
                    SIPPassword = user.SipPassword,
                    SIPFromName = user.Username,
                    SIPServer = _applicationContext.SipServer,
                };

                // 创建SIP客户端
                var sipClient = new SIPClient(_logger, sipOptions, _sipTransportManager.SIPTransport!, _webRTCSettings);
                // 添加状态消息事件处理
                sipClient.StatusMessage += (client, message) => {
                    _logger.LogInformation($"SIP客户端状态: {message}");
                };

                if (user.RegisteredAt == null || user.RegisteredAt < DateTime.UtcNow.AddHours(-2) || user.RegisteredAt < _applicationContext.StartAt) {
                    sipClient.Register();
                    user.RegisteredAt = DateTime.UtcNow;
                }

                // 保存SIP客户端实例
                _applicationContext.SipClients[user.SipUsername] = sipClient;

                // 更新用户SIP注册状态
                user.SipRegistered = true;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation($"用户 {user.Username} 的SIP账号 {user.SipUsername} 注册成功");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"用户 {user.Username} 的SIP账号注册失败");
                return false;
            }
        }

        /// <summary>
        /// 发起呼叫
        /// </summary>
        public async Task<(bool Success, string Message, RTCPeerConnection? Data)> MakeCallAsync(string destination, string sipUsername, RTCSessionDescriptionInit sdpOffer) {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == sipUsername);
            try {
                if (!_applicationContext.SipClients.TryGetValue(sipUsername, out var sipClient)) {
                    if (user == null || !user.SipRegistered) {
                        return (false, "用户未注册SIP账号或SIP账号未激活", null);
                    }

                    // 尝试重新注册
                    var registered = await RegisterUserAsync(user);
                    if (!registered || !_applicationContext.SipClients.TryGetValue(sipUsername, out sipClient)) {
                        return (false, "无法获取SIP客户端", null);
                    }
                }

                var offer = await sipClient.OfferAsync(sdpOffer);
                await _hubContext.Clients.User(user.Id.ToString()).SendAsync("sdpAnswered", offer.toJSON());

                if (sipClient.RTCPeerConnection == null) throw new InvalidOperationException("RTCPeerConnection is null, call OfferAsync first.");

                sipClient.RTCPeerConnection.onicecandidate += async (candidate) => {
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

                // 发起呼叫
                await sipClient.CallAsync(destination);
                // 返回WebRTC连接对象，用于前端与SIP通信
                return (true, "呼叫已发起", sipClient.RTCPeerConnection);
            } catch (Exception ex) {
                _logger.LogError(ex, $"发起呼叫失败: {destination}");
                await _hubContext.Clients.User(user.Id.ToString()).SendAsync("callTimeout");
                return (false, $"呼叫失败: {ex.Message}", null);
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
                    sipClient.RTCPeerConnection.setRemoteDescription(answerSdp);

                    while (!sipClient.RTCPeerConnection.IsSecureContextReady())
                        await Task.Delay(100);

                    // 接听电话
                    var result = await sipClient.AnswerAsync();
                    _logger.LogInformation($"用户 {userName} 接听电话{(result ? "成功" : "失败")}");
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
        /// 异步挂断电话（增强版本，带超时处理和监控）
        /// </summary>
        public async Task<bool> HangupCallAsync(string sipUserName, string? reason = null) {
            var hangupReason = reason ?? "User requested hangup";
            var hangupId = _monitoringService.StartHangupMonitoring(sipUserName, hangupReason);

            using var hangupCts = new CancellationTokenSource(_retryPolicy.HangupTimeout);

            try {
                _logger.LogInformation($"开始挂断电话 - 用户: {sipUserName}, 原因: {hangupReason}, 超时: {_retryPolicy.HangupTimeout.TotalSeconds}秒, 监控ID: {hangupId}");

                if (!_applicationContext.SipClients.TryGetValue(sipUserName, out var sipClient)) {
                    _logger.LogWarning($"用户 {sipUserName} 的SIP客户端不存在，无法挂断");
                    _monitoringService.LogHangupStep(hangupId, "ValidationFailed", "SIP客户端不存在");
                    await NotifyHangupStatusAsync(sipUserName, "hangupFailed", "SIP客户端不存在");
                    _monitoringService.CompleteHangupMonitoring(hangupId, false, "SIP客户端不存在");
                    return false;
                }

                _monitoringService.LogHangupStep(hangupId, "SipClientFound", $"找到SIP客户端: {sipUserName}");

                if (!sipClient.IsCallActive) {
                    _logger.LogInformation($"用户 {sipUserName} 没有活动的通话，无需挂断");
                    _monitoringService.LogHangupStep(hangupId, "NoActiveCall", "没有活动通话");
                    await NotifyHangupStatusAsync(sipUserName, "callEnded", "没有活动通话");
                    _monitoringService.CompleteHangupMonitoring(hangupId, true, "没有活动通话");
                    return true;
                }

                _monitoringService.LogHangupStep(hangupId, "ActiveCallFound", "发现活动通话");

                // 发送"正在挂断"状态通知
                await NotifyHangupStatusAsync(sipUserName, "hangupInitiated", hangupReason);
                _monitoringService.LogHangupStep(hangupId, "InitiatedNotificationSent", "已发送挂断开始通知");

                _logger.LogInformation($"用户 {sipUserName} 有活动通话，执行挂断");

                // 使用超时机制执行挂断操作
                var hangupTask = Task.Run(() => {
                    _monitoringService.LogHangupStep(hangupId, "HangupStarted", "开始执行SIP挂断");
                    sipClient.Hangup();
                    _monitoringService.LogHangupStep(hangupId, "HangupCompleted", "SIP挂断完成");
                }, hangupCts.Token);

                // 等待挂断完成或超时
                var completedTask = await Task.WhenAny(
                    hangupTask,
                    Task.Delay(_retryPolicy.HangupTimeout, hangupCts.Token)
                );

                if (completedTask == hangupTask) {
                    // 挂断操作完成
                    await hangupTask; // 确保任何异常被抛出

                    // 等待一小段时间确保挂断操作完成
                    await Task.Delay(100, hangupCts.Token);

                    // 发送"通话已结束"确认
                    await NotifyHangupStatusAsync(sipUserName, "callEnded", hangupReason);
                    _monitoringService.LogHangupStep(hangupId, "EndNotificationSent", "已发送通话结束通知");

                    _logger.LogInformation($"用户 {sipUserName} 挂断电话成功");
                    _monitoringService.CompleteHangupMonitoring(hangupId, true);
                    return true;
                } else {
                    // 挂断操作超时
                    _logger.LogWarning($"用户 {sipUserName} 挂断操作超时，执行强制终止");
                    _monitoringService.LogHangupStep(hangupId, "HangupTimeout", "挂断操作超时");

                    // 强制终止连接
                    await ForceTerminateConnectionAsync(sipUserName, sipClient);
                    _monitoringService.LogHangupStep(hangupId, "ForceTerminated", "已强制终止连接");

                    // 发送超时通知
                    await NotifyHangupStatusAsync(sipUserName, "hangupFailed", "挂断操作超时，已强制终止连接");

                    _monitoringService.CompleteHangupMonitoring(hangupId, false, "挂断操作超时");
                    return false;
                }
            } catch (OperationCanceledException) when (hangupCts.Token.IsCancellationRequested) {
                _logger.LogWarning($"用户 {sipUserName} 挂断操作被取消或超时");
                _monitoringService.LogHangupStep(hangupId, "OperationCancelled", "操作被取消或超时");
                await NotifyHangupStatusAsync(sipUserName, "hangupFailed", "挂断操作超时");
                _monitoringService.CompleteHangupMonitoring(hangupId, false, "操作被取消");
                return false;
            } catch (Exception ex) {
                _logger.LogError(ex, $"用户 {sipUserName} 挂断电话失败: {ex.Message}");
                _monitoringService.LogHangupStep(hangupId, "ExceptionOccurred", $"异常: {ex.Message}");

                // 发送挂断失败通知
                await NotifyHangupStatusAsync(sipUserName, "hangupFailed", $"挂断失败: {ex.Message}");
                _monitoringService.CompleteHangupMonitoring(hangupId, false, ex.Message);
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

                // 如果有网络连接，尝试发送BYE消息（但不等待响应）
                if (sipClient.IsNetworkConnected) {
                    try {
                        using var forceTerminateCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await Task.Run(() => sipClient.Hangup(), forceTerminateCts.Token);
                    } catch (Exception ex) {
                        _logger.LogWarning(ex, $"强制终止时发送BYE消息失败，但继续清理资源");
                    }
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
                sipClient.RTCPeerConnection?.addIceCandidate(candidate);
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
                    return sipClient.RTCPeerConnection!.IsSecureContextReady();
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
    }
}