using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using System.Security.Claims;

namespace AI.Caller.Phone.Hubs {
    [Authorize]
    public class WebRtcHub : Hub {
        private readonly SipService _sipService;
        private readonly AppDbContext _appDbContext;
        private readonly ISimpleRecordingService _recordingService;
        private readonly ILogger<WebRtcHub> _logger;

        public WebRtcHub(SipService sipService, AppDbContext appDbContext, ISimpleRecordingService recordingService, ILogger<WebRtcHub> logger) {
            _sipService = sipService;
            _appDbContext = appDbContext;
            _recordingService = recordingService;
            _logger = logger;
        }

        public async Task AnswerAsync(WebRtcAnswerModel model) {
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            if (RTCSessionDescriptionInit.TryParse(model.AnswerSdp, out var v)) {
                var user = await _appDbContext.Users.FirstAsync(x => x.Id == userId);
                var result = await _sipService.AnswerAsync(user, v);
                if (result && model.CallerId.HasValue) {
                    var caller = await _appDbContext.Users.FirstAsync(x => x.Id == model.CallerId.Value);
                    await Clients.User(caller.Id.ToString()).SendAsync("answered");
                }
            }
        }

        public async Task SendIceCandidateAsync(RTCIceCandidateInit candidate) {
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            var user = await _appDbContext.Users.FirstAsync(x => x.Id == userId);
            _sipService.AddIceCandidate(user, candidate);
            await Task.CompletedTask;
        }

        public async Task<bool> GetSecureContextState() {
            var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
            var user = await _appDbContext.Users.FirstAsync(x => x.Id == userId);
            var result = _sipService.GetSecureContextReady(user);
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

                var result = await _sipService.HangupWithNotificationAsync(user, model);

                if (!result) {
                    await Clients.Caller.SendAsync("hangupFailed", new {
                        message = "挂断操作失败",
                        timestamp = DateTime.UtcNow
                    });
                }

                return result;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error during hangup operation for user {UserName}", Context.User!.Identity!.Name);
                await Clients.Caller.SendAsync("hangupFailed", new {
                    message = $"挂断过程中发生错误: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
                return false;
            }
        }

        public async Task SendHangupInitiatedAsync(string message) {
            try {
                await Clients.Caller.SendAsync("hangupInitiated", new {
                    message = message,
                    timestamp = DateTime.UtcNow
                });
            } catch (Exception ex) {
                Console.WriteLine($"Error sending hangup initiated notification: {ex.Message}");
            }
        }

        public async Task SendCallEndedAsync(string reason) {
            try {
                await Clients.Caller.SendAsync("callEnded", new {
                    reason = reason,
                    timestamp = DateTime.UtcNow
                });
            } catch (Exception ex) {
                Console.WriteLine($"Error sending call ended notification: {ex.Message}");
            }
        }

        public async Task SendHangupFailedAsync(string message) {
            try {
                await Clients.Caller.SendAsync("hangupFailed", new {
                    message = message,
                    timestamp = DateTime.UtcNow
                });
            } catch (Exception ex) {
                Console.WriteLine($"Error sending hangup failed notification: {ex.Message}");
            }
        }

        public async Task<object> StartRecordingAsync() {
            try {
                var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);

                var user = await _appDbContext.Users.Include(x => x.SipAccount).FirstOrDefaultAsync(x => x.Id == userId);
                if (user == null || (user.SipAccount == null || string.IsNullOrEmpty(user.SipAccount.SipUsername))) {
                    return new { success = false, message = "用户SIP账号信息不存在" };
                }

                var result = await _recordingService.StartRecordingAsync(user.Id);

                if (result) {
                    await Clients.Caller.SendAsync("recordingStarted", new {
                        message = "录音已开始",
                        timestamp = DateTime.UtcNow
                    });
                    return new { success = true, message = "录音已开始" };
                } else {
                    return new { success = false, message = "录音开始失败" };
                }
            } catch (Exception ex) {
                return new { success = false, message = $"录音开始失败: {ex.Message}" };
            }
        }

        public async Task<object> StopRecordingAsync() {
            try {
                var userId = Context.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                var user = await _appDbContext.Users.Include(x => x.SipAccount).FirstOrDefaultAsync(x => x.Id == userId);
                if (user == null || (user.SipAccount == null || string.IsNullOrEmpty(user.SipAccount.SipUsername))) {
                    return new { success = false, message = "用户SIP账号信息不存在" };
                }

                var result = await _recordingService.StopRecordingAsync(user.Id);

                if (result) {
                    await Clients.Caller.SendAsync("recordingStopped", new {
                        message = "录音已停止",
                        timestamp = DateTime.UtcNow
                    });
                    return new { success = true, message = "录音已停止" };
                } else {
                    return new { success = false, message = "录音停止失败" };
                }
            } catch (Exception ex) {
                return new { success = false, message = $"录音停止失败: {ex.Message}" };
            }
        }
    }

    public record WebRtcAnswerModel(int? CallerId, string AnswerSdp);

    public record WebRtcHangupModel(string Target, string? Reason = null, HangupCallContext? CallContext = null);

    public record HangupCallContext(string? CallId, CallerInfo? Caller, CallerInfo? Callee, DateTime? Timestamp, bool IsExternal);

    public record CallerInfo(string? UserId, string? SipUsername);
}