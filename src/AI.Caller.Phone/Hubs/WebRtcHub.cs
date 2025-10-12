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

        public WebRtcHub(
            ILogger<WebRtcHub> logger,
            ICallManager callManager,
            AppDbContext appDbContext,
            ApplicationContext applicationContext,
            ISimpleRecordingService recordingService
            ) {
            _logger           = logger;
            _callManager      = callManager;
            _appDbContext     = appDbContext;
            _recordingService = recordingService;
            _applicationContext = applicationContext;
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
    }

    public record IceCandidateModel(string CallId, string iceCandidate);

    public record WebRtcAnswerModel(string CallId, string AnswerSdp);

    public record WebRtcHangupModel(string CallId, string Target, string? Reason = null);

    public record CallerInfo(string? UserId, string? SipUsername);
}