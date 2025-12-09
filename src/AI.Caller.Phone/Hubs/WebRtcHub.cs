using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.Net;
using System.Security.Claims;

namespace AI.Caller.Phone.Hubs {
    [Authorize]
    public class WebRtcHub : Hub {
        private readonly ILogger _logger;
        private readonly ICallManager _callManager;
        private readonly AppDbContext _appDbContext;
        private readonly ApplicationContext _applicationContext;
        private readonly ISimpleRecordingService _recordingService;
        private readonly AICustomerServiceManager _aiServiceManager;
        private readonly IMonitoringService _monitoringService;
        private readonly IPlaybackControlService _playbackControlService;

        public WebRtcHub(
            ILogger<WebRtcHub> logger,
            ICallManager callManager,
            AppDbContext appDbContext,
            ApplicationContext applicationContext,
            ISimpleRecordingService recordingService,
            AICustomerServiceManager aiServiceManager,
            IMonitoringService monitoringService,
            IPlaybackControlService playbackControlService
            ) {
            _logger           = logger;
            _callManager      = callManager;
            _appDbContext     = appDbContext;
            _recordingService = recordingService;
            _applicationContext = applicationContext;
            _aiServiceManager = aiServiceManager;
            _monitoringService = monitoringService;
            _playbackControlService = playbackControlService;
        }

        public async Task AnswerAsync(WebRtcAnswerModel model) {
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            if (RTCSessionDescriptionInit.TryParse(model.AnswerSdp, out var answerSdp)) {
                await _callManager.AnswerAsync(model.CallId, answerSdp);
            }
        }

        public async Task SendIceCandidateAsync(IceCandidateModel model) {
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
        /// 发送监听音频到特定用户
        /// </summary>
        private async Task SendMonitoringAudioAsync(int monitorUserId, byte[] g711AudioData) {
            try {
                // G.711 A-law解码为PCM（服务端解码）
                var pcmData = DecodeG711ALaw(g711AudioData);
                
                // Base64编码PCM数据
                var base64Audio = Convert.ToBase64String(pcmData);

                // 发送给特定监听者
                await Clients.User(monitorUserId.ToString())
                    .SendAsync("monitoringAudio", base64Audio);

                _logger.LogTrace("音频已发送到监听者: UserId {UserId}, G.711大小 {G711Size} 字节, PCM大小 {PcmSize} 字节",
                    monitorUserId, g711AudioData.Length, pcmData.Length);
            } catch (Exception ex) {
                _logger.LogError(ex, "发送监听音频失败: UserId {UserId}", monitorUserId);
            }
        }

        /// <summary>
        /// G.711 A-law解码为PCM（16位，8000Hz）
        /// </summary>
        private byte[] DecodeG711ALaw(byte[] alawData) {
            var pcmData = new byte[alawData.Length * 2];  // 16位PCM = 2字节per sample
            
            for (int i = 0; i < alawData.Length; i++) {
                short pcmSample = DecodeALawSample(alawData[i]);
                // 小端序写入
                pcmData[i * 2] = (byte)(pcmSample & 0xFF);
                pcmData[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
            }
            
            return pcmData;
        }

        /// <summary>
        /// 解码单个A-law样本
        /// </summary>
        private short DecodeALawSample(byte alaw) {
            alaw ^= 0x55;  // A-law反转
            
            int sign = (alaw & 0x80) != 0 ? -1 : 1;
            int exponent = (alaw & 0x70) >> 4;
            int mantissa = alaw & 0x0F;
            
            int sample = mantissa << 4;
            if (exponent > 0) {
                sample += 0x100;
                sample <<= (exponent - 1);
            }
            
            return (short)(sign * sample);
        }

        /// <summary>
        /// 开始监听通话
        /// </summary>
        public async Task<object> StartMonitoringAsync(int targetUserId, string callId) {
            try {
                var monitorUserId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                var monitorUserName = Context.User!.Identity!.Name ?? "Unknown";

                var session = await _aiServiceManager.StartMonitoringAsync(
                    targetUserId,
                    monitorUserId,
                    monitorUserName,
                    callId);

                // 加入监听组
                await Groups.AddToGroupAsync(Context.ConnectionId, $"monitoring_{callId}");

                // 订阅音频事件
                var aiSession = _aiServiceManager.GetActiveSession(targetUserId);
                if (aiSession?.AudioBridge is AI.Caller.Core.AudioBridge audioBridge) {
                    // 订阅监听音频事件
                    Action<int, byte[]> audioHandler = async (userId, audioData) => {
                        if (userId == monitorUserId) {
                            await SendMonitoringAudioAsync(monitorUserId, audioData);
                        }
                    };

                    audioBridge.MonitoringAudioReady += audioHandler;

                    _logger.LogInformation("已订阅音频事件: MonitorUserId {MonitorUserId}, TargetUserId {TargetUserId}",
                        monitorUserId, targetUserId);
                } else {
                    _logger.LogWarning("无法找到AudioBridge，音频流传输可能不可用: TargetUserId {TargetUserId}", targetUserId);
                }

                _logger.LogInformation("用户 {MonitorUserId} 开始监听通话 {CallId}", monitorUserId, callId);

                return new { success = true, sessionId = session.Id, message = "监听已开始，正在接收音频..." };
            } catch (Exception ex) {
                _logger.LogError(ex, "开始监听失败");
                return new { success = false, message = $"监听失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 停止监听通话
        /// </summary>
        public async Task<object> StopMonitoringAsync(int targetUserId, int sessionId, string callId) {
            try {
                var monitorUserId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);

                await _aiServiceManager.StopMonitoringAsync(targetUserId, monitorUserId, sessionId);

                // 取消订阅音频事件
                var aiSession = _aiServiceManager.GetActiveSession(targetUserId);
                if (aiSession?.AudioBridge is AI.Caller.Core.AudioBridge audioBridge) {
                    // 注意：这里简化处理，实际应该保存handler引用以便取消订阅
                    // 由于AudioBridge.RemoveMonitor已经移除了监听者，不会再收到音频
                    _logger.LogInformation("音频事件订阅已清理: MonitorUserId {MonitorUserId}", monitorUserId);
                }

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

                // 通知监听组
                await Clients.Group($"monitoring_{callId}").SendAsync("interventionStarted", new {
                    userId = monitorUserId,
                    reason,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation("用户 {MonitorUserId} 接入通话 {CallId}", monitorUserId, callId);

                return new { success = true, message = "接入成功" };
            } catch (Exception ex) {
                _logger.LogError(ex, "人工接入失败");
                return new { success = false, message = $"接入失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 退出人工接入
        /// </summary>
        public async Task<object> ExitInterventionAsync(int targetUserId, string callId, List<int>? skipSegmentIds, bool resumePlayback) {
            try {
                var monitorUserId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);

                await _aiServiceManager.ExitInterventionAsync(targetUserId, callId, skipSegmentIds, resumePlayback);

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
        /// 暂停播放
        /// </summary>
        public async Task<object> PausePlaybackAsync(string callId) {
            try {
                await _playbackControlService.PausePlaybackAsync(callId);

                await Clients.Group($"monitoring_{callId}").SendAsync("playbackPaused", new {
                    callId,
                    timestamp = DateTime.UtcNow
                });

                return new { success = true, message = "播放已暂停" };
            } catch (Exception ex) {
                _logger.LogError(ex, "暂停播放失败");
                return new { success = false, message = $"暂停播放失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 恢复播放
        /// </summary>
        public async Task<object> ResumePlaybackAsync(string callId) {
            try {
                await _playbackControlService.ResumePlaybackAsync(callId);

                await Clients.Group($"monitoring_{callId}").SendAsync("playbackResumed", new {
                    callId,
                    timestamp = DateTime.UtcNow
                });

                return new { success = true, message = "播放已恢复" };
            } catch (Exception ex) {
                _logger.LogError(ex, "恢复播放失败");
                return new { success = false, message = $"恢复播放失败: {ex.Message}" };
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

        #endregion
    }

    public record IceCandidateModel(string CallId, string iceCandidate);

    public record WebRtcAnswerModel(string CallId, string AnswerSdp);

    public record WebRtcHangupModel(string CallId, string Target, string? Reason = null);

    public record CallerInfo(string? UserId, string? SipUsername);
}