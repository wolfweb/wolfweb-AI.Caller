using AI.Caller.Core;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Encoders;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.Net;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Channels;

namespace AI.Caller.Phone.Hubs {
    [Authorize]
    public class WebRtcHub : Hub {
        private readonly ILogger _logger;
        private readonly ICallManager _callManager;
        private readonly AppDbContext _appDbContext;
        private readonly IHubContext<WebRtcHub> _hubContext;
        private readonly ApplicationContext _applicationContext;
        private readonly ISimpleRecordingService _recordingService;
        private readonly AICustomerServiceManager _aiServiceManager;
        private readonly IPlaybackControlService _playbackControlService;

        private readonly AudioCodecFactory _codecFactory;
        private readonly WebRTCSettings _webRtcSettings;

        public WebRtcHub(
            ILogger<WebRtcHub> logger,
            AudioCodecFactory codecFactory,
            ICallManager callManager,
            AppDbContext appDbContext,
            IHubContext<WebRtcHub> hubContext,
            ApplicationContext applicationContext,
            ISimpleRecordingService recordingService,
            AICustomerServiceManager aiServiceManager,
            IPlaybackControlService playbackControlService,
            Microsoft.Extensions.Options.IOptions<WebRTCSettings> webRtcSettings
            ) {
            _logger                 = logger;
            _hubContext             = hubContext;
            _callManager            = callManager;
            _codecFactory           = codecFactory;
            _appDbContext           = appDbContext;
            _recordingService       = recordingService;
            _aiServiceManager       = aiServiceManager;
            _applicationContext     = applicationContext;
            _playbackControlService = playbackControlService;
            _webRtcSettings         = webRtcSettings.Value;
        }

        public async Task AnswerAsync(WebRtcAnswerModel model) {
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            if (RTCSessionDescriptionInit.TryParse(model.AnswerSdp, out var answerSdp)) {
                await _callManager.AnswerAsync(model.CallId, answerSdp);
            }
        }

        public async Task SendIceCandidateAsync(IceCandidateModel model) {
            if (string.IsNullOrEmpty(model.CallId)) return;
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            if (RTCIceCandidateInit.TryParse(model.iceCandidate, out var candidate)) {
                _callManager.AddIceCandidate(model.CallId, userId, candidate);
            }
            await Task.CompletedTask;
        }

        public async Task<bool> GetSecureContextState(string callId) {
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            var result = _callManager.GetSecureContextState(callId, userId);
            return await Task.FromResult(result);
        }

        public async Task<bool> HangupCallAsync(WebRtcHangupModel model) {
            try {
                var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                var user = await _appDbContext.Users.Include(x => x.SipAccount).FirstOrDefaultAsync(x => x.Id == userId);
                if (user == null || (user.SipAccount == null || string.IsNullOrEmpty(user.SipAccount.SipUsername))) {
                    await Clients.Caller.SendAsync("hangupFailed", new {
                        message = "用户SIP账号信息不存在",
                        timestamp = DateTime.UtcNow
                    });
                    return false;
                }

                await _callManager.HangupCallAsync(model.CallId, userId);

                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error during hangup operation for user {UserName}", Context.User!.Identity!.Name);
                await Clients.Caller.SendAsync("hangupFailed", new {
                    message = $"挂断过程中发生错误: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
                return false;
            }
        }

        public async Task<object> PauseRecordingAsync() {
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            try {                
                if (!Context.User!.HasClaim("isAdmin","True")) {
                    _logger.LogWarning("普通用户 {UserId} 尝试暂停录音，权限不足", userId);
                    return new { success = false, message = "权限不足：只有管理员可以控制录音" };
                }

                var result = await _recordingService.PauseRecordingAsync(userId);

                if (result) {
                    await Clients.Caller.SendAsync("recordingPaused", new {
                        message = "录音已暂停",
                        timestamp = DateTime.UtcNow
                    });
                    _logger.LogInformation("管理员 {UserId} 暂停了录音", userId);
                    return new { success = true, message = "录音已暂停" };
                } else {
                    return new { success = false, message = "录音暂停失败" };
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error pausing recording for user {UserId}", userId);
                return new { success = false, message = $"录音暂停失败: {ex.Message}" };
            }
        }

        public async Task<object> ResumeRecordingAsync() {
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            try {
                if (!Context.User!.HasClaim("isAdmin", "True")) {
                    _logger.LogWarning("普通用户 {UserId} 尝试恢复录音，权限不足", userId);
                    return new { success = false, message = "权限不足：只有管理员可以控制录音" };
                }

                var result = await _recordingService.ResumeRecordingAsync(userId);

                if (result) {
                    await Clients.Caller.SendAsync("recordingResumed", new {
                        message = "录音已恢复",
                        timestamp = DateTime.UtcNow
                    });
                    _logger.LogInformation("管理员 {UserId} 恢复了录音", userId);
                    return new { success = true, message = "录音已恢复" };
                } else {
                    return new { success = false, message = "录音恢复失败" };
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error resuming recording for user {UserId}", userId);
                return new { success = false, message = $"录音恢复失败: {ex.Message}" };
            }
        }

        public async Task ReconnectWebRTCAsync() {
            await Task.CompletedTask;
        }

        public async Task<bool> Heartbeat() {
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            _applicationContext.AddActiviteUser(userId);
            return await Task.FromResult(true);
        }

        public override Task OnConnectedAsync() {
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            _applicationContext.AddActiviteUser(userId);
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception) {
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            _applicationContext.RemoveActiviteUserId(userId);
            return base.OnDisconnectedAsync(exception);
        }

        #region 监听与接入功能
        /// <summary>
        /// 建立WebRTC监听连接
        /// </summary>
        public async Task<object> ConnectMonitoringWebRtc(int targetUserId, string callId, string offerSdp) {
             if (RTCSessionDescriptionInit.TryParse(offerSdp, out var offer)) {
                return await StartMonitoringInternal(targetUserId, callId, offer);
             }
             return new { success = false, message = "Invalid SDP" };
        }

        /// <summary>
        /// 用于监控的ICE Candidate
        /// </summary>
        public async Task SendMonitorIceCandidate(int targetUserId, string iceCandidate) {
            try {
                var monitorUserId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                if (RTCIceCandidateInit.TryParse(iceCandidate, out var candidate)) {
                    var session = _aiServiceManager.GetActiveSession(targetUserId);
                    if (session?.AudioBridge is AudioBridge bridge) {
                        var monitorSession = bridge.GetMonitorSession(monitorUserId);
                        if (monitorSession != null) {
                            monitorSession.AddIceCandidate(candidate);
                            _logger.LogTrace("Added ICE candidate for monitor session: MonitorUser {MonitorUserId}", monitorUserId);
                        } else {
                            _logger.LogWarning("Monitor session not found for ICE candidate: MonitorUser {MonitorUserId}", monitorUserId);
                        }
                    }
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to add ICE candidate for monitoring");
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// 停止监听通话
        /// </summary>
        public async Task<object> StopMonitoringAsync(int targetUserId, int sessionId, string callId) {
            try {
                var monitorUserId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);

                await _aiServiceManager.StopMonitoringAsync(targetUserId, monitorUserId, sessionId);

                // 离开监听组
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"monitoring_{callId}");

                _logger.LogInformation("用户 {MonitorUserId} 停止监听通话 {CallId}", monitorUserId, callId);

                return new { success = true, message = "监听已停止" };
            } catch (Exception ex) {
                _logger.LogError(ex, "停止监听失败");
                return new { success = false, message = $"停止监听失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 人工接入通话
        /// </summary>
        public async Task<object> InterventAsync(int targetUserId, int sessionId, string reason, string callId) {
            try {
                var monitorUserId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);

                await _aiServiceManager.InterventAsync(targetUserId, monitorUserId, sessionId, reason, callId);

                var playbackState = await _playbackControlService.GetPlaybackControlAsync(callId);
                int? currentSegmentId = playbackState?.CurrentSegmentId;

                // 通知监听组
                await Clients.Group($"monitoring_{callId}").SendAsync("interventionStarted", new {
                    userId = monitorUserId,
                    reason,
                    currentSegmentId,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation("用户 {MonitorUserId} 接入通话 {CallId}", monitorUserId, callId);

                return new { success = true, message = "接入成功", currentSegmentId };
            } catch (Exception ex) {
                _logger.LogError(ex, "人工接入失败");
                return new { success = false, message = $"接入失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 退出人工接入
        /// </summary>
        public async Task<object> ExitInterventionAsync(int targetUserId, string callId, List<int>? playSegmentIds, bool resumePlayback) {
            try {
                var monitorUserId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);

                await _aiServiceManager.ExitInterventionAsync(targetUserId, callId, playSegmentIds, resumePlayback);

                // 通知监听组
                await Clients.Group($"monitoring_{callId}").SendAsync("interventionEnded", new {
                    userId = monitorUserId,
                    resumePlayback,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation("用户 {MonitorUserId} 退出接入通话 {CallId}", monitorUserId, callId);

                return new { success = true, message = "已退出接入" };
            } catch (Exception ex) {
                _logger.LogError(ex, "退出接入失败");
                return new { success = false, message = $"退出接入失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 获取播放状态
        /// </summary>
        public async Task<object> GetPlaybackStateAsync(string callId) {
            try {
                var state = await _playbackControlService.GetPlaybackControlAsync(callId);
                return new { success = true, state };
            } catch (Exception ex) {
                _logger.LogError(ex, "获取播放状态失败");
                return new { success = false, message = $"获取播放状态失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 跳过片段
        /// </summary>
        public async Task<object> SkipSegmentAsync(string callId, int segmentId) {
            try {
                await _playbackControlService.SkipSegmentAsync(callId, segmentId);

                await Clients.Group($"monitoring_{callId}").SendAsync("segmentSkipped", new {
                    callId,
                    segmentId,
                    timestamp = DateTime.UtcNow
                });

                return new { success = true, message = "片段已跳过" };
            } catch (Exception ex) {
                _logger.LogError(ex, "跳过片段失败");
                return new { success = false, message = $"跳过片段失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 发送人工接入音频到客户（直接接收二进制数据，提高性能）
        /// </summary>
        public Task<object> SendInterventionAudioBinary(int targetUserId, string callId, byte[] audioData) {
            try {
                var monitorUserId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);

                var session = _aiServiceManager.GetActiveSession(targetUserId);
                if (session?.AudioBridge is AudioBridge audioBridge) {
                    audioBridge.ProcessInterventionAudio(audioData);
                    
                    _logger.LogTrace("人工接入音频已发送（二进制）: MonitorUserId {MonitorUserId}, TargetUserId {TargetUserId}, 大小 {Size} 字节", monitorUserId, targetUserId, audioData.Length);
                }

                return Task.FromResult<object>(new { success = true });
            } catch (Exception ex) {
                _logger.LogError(ex, "发送人工接入音频失败（二进制）");
                return Task.FromResult<object>(new { success = false, message = $"发送音频失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 发送人工接入音频到客户（Base64格式，向后兼容）
        /// </summary>
        public Task<object> SendInterventionAudio(int targetUserId, string callId, string base64Audio) {
            try {
                var monitorUserId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);

                var audioData = Convert.FromBase64String(base64Audio);

                var session = _aiServiceManager.GetActiveSession(targetUserId);
                if (session?.AudioBridge is AudioBridge audioBridge) {
                    audioBridge.ProcessInterventionAudio(audioData);
                    
                    _logger.LogTrace("人工接入音频已发送（Base64）: MonitorUserId {MonitorUserId}, TargetUserId {TargetUserId}, 大小 {Size} 字节", monitorUserId, targetUserId, audioData.Length);
                }

                return Task.FromResult<object>(new { success = true });
            } catch (Exception ex) {
                _logger.LogError(ex, "发送人工接入音频失败（Base64）");
                return Task.FromResult<object>(new { success = false, message = $"发送音频失败: {ex.Message}" });
            }
        }

        #endregion

        #region DTMF功能

        /// <summary>
        /// 发送DTMF信号（仅在WebRTC不可用时使用）
        /// </summary>
        public async Task<object> SendDtmfTone(DtmfToneModel model) {
            try {
                var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                
                byte dtmfByte = ConvertCharToDtmfByte(model.Tone);
                
                await _callManager.SendDtmfAsync(dtmfByte, userId, model.CallId);
                
                _logger.LogInformation("用户 {UserId} 通过SIP发送DTMF: {Tone} (CallId: {CallId})", userId, model.Tone, model.CallId);
                
                return new { success = true, message = "DTMF已通过SIP发送" };
            } catch (Exception ex) {
                _logger.LogError(ex, "发送DTMF失败: CallId {CallId}, Tone {Tone}", model.CallId, model.Tone);
                return new { success = false, message = $"DTMF发送失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 记录DTMF发送日志（不实际发送DTMF）
        /// </summary>
        public async Task<object> LogDtmfTone(DtmfLogModel model) {
            try {
                var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                
                _logger.LogInformation("用户 {UserId} 发送DTMF: {Tone} 通过 {Method} (CallId: {CallId})", userId, model.Tone, model.Method, model.CallId);
                
                // await _recordingService.LogDtmfEventAsync(userId, model.CallId, model.Tone, model.Method);
                
                return await Task.FromResult(new { success = true, message = "DTMF日志已记录" });
            } catch (Exception ex) {
                _logger.LogError(ex, "记录DTMF日志失败: CallId {CallId}, Tone {Tone}", model.CallId, model.Tone);
                return new { success = false, message = $"日志记录失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 清空DTMF输入缓冲区（通知后端DTMF收集器重置）
        /// </summary>
        public async Task<object> ClearDtmfInput(DtmfClearModel model) {
            try {
                var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                                
                _logger.LogInformation("用户 {UserId} 请求清空DTMF输入，CallId: {CallId}", userId, model.CallId);
                
                await _callManager.ResetDtmfCollectionAsync(model.CallId);
                
                if (!string.IsNullOrEmpty(model.ClearSequence)) {
                    _logger.LogInformation("发送DTMF清空序列: {Sequence}", model.ClearSequence);
                    for (int i = 0; i < model.ClearSequence.Length; i++) {
                        byte dtmfByte = ConvertCharToDtmfByte(model.ClearSequence[i].ToString());
                        await _callManager.SendDtmfAsync(dtmfByte, userId, model.CallId);
                        
                        if (i < model.ClearSequence.Length - 1) {
                            await Task.Delay(100);
                        }
                    }
                }
                                
                return new { success = true, message = "DTMF输入已清空" };
            } catch (Exception ex) {
                _logger.LogError(ex, "清空DTMF输入失败: CallId {CallId}", model.CallId);
                return new { success = false, message = $"清空DTMF失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 开始DTMF收集
        /// </summary>
        public async Task<object> StartDtmfCollection(DtmfCollectionStartModel model) {
            try {
                var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                
                var config = new AI.Caller.Core.Services.DtmfCollectionConfig {
                    MaxLength = model.MaxLength ?? 18,
                    TerminationKey = model.TerminationKey ?? '#',
                    BackspaceKey = model.BackspaceKey ?? '*',
                    Timeout = model.TimeoutSeconds.HasValue ? TimeSpan.FromSeconds(model.TimeoutSeconds.Value) : TimeSpan.FromSeconds(30),
                    EnableLogging = true,
                    Description = model.Description ?? $"用户 {userId} 的DTMF收集"
                };

                _logger.LogInformation("用户 {UserId} 开始DTMF收集: {CallId}", userId, model.CallId);

                _ = StartDtmfCollectionInBackground(model.CallId, config, userId);

                return new { success = true, message = "DTMF收集已开始" };
            } catch (Exception ex) {
                _logger.LogError(ex, "开始DTMF收集失败: CallId {CallId}", model.CallId);
                return new { success = false, message = $"开始DTMF收集失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 停止DTMF收集
        /// </summary>
        public async Task<object> StopDtmfCollection(string callId) {
            try {
                var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                
                await _callManager.StopDtmfCollectionAsync(callId);
                
                _logger.LogInformation("用户 {UserId} 停止DTMF收集: {CallId}", userId, callId);
                
                return new { success = true, message = "DTMF收集已停止" };
            } catch (Exception ex) {
                _logger.LogError(ex, "停止DTMF收集失败: CallId {CallId}", callId);
                return new { success = false, message = $"停止DTMF收集失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 获取当前DTMF输入
        /// </summary>
        public async Task<object> GetCurrentDtmfInput(string callId) {
            try {
                var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                
                var input = await _callManager.GetCurrentDtmfInputAsync(callId);
                var isCollecting = await _callManager.IsDtmfCollectingAsync(callId);
                
                return new { 
                    success = true, 
                    input = input,
                    isCollecting = isCollecting,
                    timestamp = DateTime.UtcNow
                };
            } catch (Exception ex) {
                _logger.LogError(ex, "获取DTMF输入失败: CallId {CallId}", callId);
                return new { success = false, message = $"获取DTMF输入失败: {ex.Message}" };
            }
        }

        #endregion

        /// <summary>
        /// 后台执行DTMF收集
        /// </summary>
        private async Task StartDtmfCollectionInBackground(string callId, AI.Caller.Core.Services.DtmfCollectionConfig config, int userId) {
            try {
                var result = await _callManager.StartDtmfCollectionAsync(callId, config);
                
                await _hubContext.Clients.User(userId.ToString()).SendAsync("dtmfCollectionCompleted", new {
                    callId = callId,
                    input = result,
                    timestamp = DateTime.UtcNow
                });
            } catch (TimeoutException) {
                _logger.LogWarning("DTMF收集超时: {CallId}", callId);
                try {
                    await _hubContext.Clients.User(userId.ToString()).SendAsync("dtmfCollectionTimeout", new {
                        callId = callId,
                        message = "DTMF输入超时",
                        timestamp = DateTime.UtcNow
                    });
                } catch (Exception notifyEx) {
                    _logger.LogError(notifyEx, "发送超时通知失败: {CallId}", callId);
                }
            } catch (OperationCanceledException) {
                _logger.LogInformation("DTMF收集被取消: {CallId}", callId);
                try {
                    await _hubContext.Clients.User(userId.ToString()).SendAsync("dtmfCollectionCancelled", new {
                        callId = callId,
                        message = "DTMF收集被取消",
                        timestamp = DateTime.UtcNow
                    });
                } catch (Exception notifyEx) {
                    _logger.LogError(notifyEx, "发送取消通知失败: {CallId}", callId);
                }
            } catch (InvalidOperationException ex) when (ex.Message.Contains("已经在进行DTMF收集")) {
                _logger.LogWarning("DTMF收集冲突: {CallId}, {Message}", callId, ex.Message);
                try {
                    await _hubContext.Clients.User(userId.ToString()).SendAsync("dtmfCollectionError", new {
                        callId = callId,
                        error = "当前通话已在进行DTMF收集，请先停止现有收集",
                        timestamp = DateTime.UtcNow
                    });
                } catch (Exception notifyEx) {
                    _logger.LogError(notifyEx, "发送冲突通知失败: {CallId}", callId);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "DTMF收集异常: {CallId}", callId);
                try {
                    await _hubContext.Clients.User(userId.ToString()).SendAsync("dtmfCollectionError", new {
                        callId = callId,
                        error = ex.Message,
                        timestamp = DateTime.UtcNow
                    });
                } catch (Exception notifyEx) {
                    _logger.LogError(notifyEx, "发送错误通知失败: {CallId}", callId);
                }
            }
        }

        private async Task<object> StartMonitoringInternal(int targetUserId, string callId, RTCSessionDescriptionInit offer) {
            try {
                var monitorUserId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                var monitorUserName = Context.User!.Identity!.Name ?? "Unknown";

                string? answerSdp = null;
                var connectionId = Context.ConnectionId;
                var mediaSession = new MonitorMediaSession(_logger, _codecFactory, _webRtcSettings);
                var answer = await mediaSession.HandleOfferAsync(offer);
                answerSdp = answer.sdp;

                mediaSession.OnIceCandidate += async (candidate) => {
                    await _hubContext.Clients.Client(connectionId).SendAsync("receiveIceCandidate", candidate.toJSON());
                };

                var session = await _aiServiceManager.StartMonitoringAsync(
                    targetUserId,
                    monitorUserId,
                    monitorUserName,
                    callId,
                    mediaSession); // 传入 Session

                await Groups.AddToGroupAsync(Context.ConnectionId, $"monitoring_{callId}");

                if (mediaSession != null) {
                    _logger.LogInformation("WebRTC监听已就绪: MonitorUserId {MonitorUserId}", monitorUserId);
                }

                _logger.LogInformation("用户 {MonitorUserId} 开始监听通话 {CallId}", monitorUserId, callId);
                return new { success = true, sessionId = session.Id, answer = answerSdp, message = "监听已开始" };
            } catch (Exception ex) {
                _logger.LogError(ex, "开始监听失败");
                return new { success = false, message = $"监听失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 将DTMF字符转换为byte值（与DtmfCollector保持一致）
        /// </summary>
        private static byte ConvertCharToDtmfByte(string tone) {
            return tone switch {
                "0" => 0,
                "1" => 1,
                "2" => 2,
                "3" => 3,
                "4" => 4,
                "5" => 5,
                "6" => 6,
                "7" => 7,
                "8" => 8,
                "9" => 9,
                "*" => 10,
                "#" => 11,
                "A" => 12,
                "B" => 13,
                "C" => 14,
                "D" => 15,
                _ => throw new ArgumentException($"Invalid DTMF tone: {tone}")
            };
        }        
    }

    public record IceCandidateModel(string CallId, string iceCandidate);

    public record WebRtcAnswerModel(string CallId, string AnswerSdp);

    public record WebRtcHangupModel(string CallId, string Target, string? Reason = null);

    public record DtmfToneModel(string CallId, string Tone);

    public record DtmfLogModel(string CallId, string Tone, string Method);

    public record DtmfClearModel(string CallId, string? ClearSequence = null);

    public record DtmfCollectionStartModel(
        string CallId, 
        int? MaxLength = null, 
        char? TerminationKey = null, 
        char? BackspaceKey = null, 
        int? TimeoutSeconds = null,
        string? Description = null
    );
}