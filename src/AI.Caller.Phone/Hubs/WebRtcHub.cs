using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.Net;

namespace AI.Caller.Phone.Hubs {
    [Authorize]
    public class WebRtcHub : Hub {
        private readonly SipService _sipService;
        private readonly AppDbContext _appDbContext;
        
        public WebRtcHub(SipService sipService, AppDbContext appDbContext) {
            _sipService = sipService;
            _appDbContext = appDbContext;
        }

        public async Task AnswerAsync(WebRtcAnswerModel model) {
            if (RTCSessionDescriptionInit.TryParse(model.AnswerSdp, out var v)) {
                var result = await _sipService.AnswerAsync(Context.User.Identity.Name, v);
                if (result) {
                    var user = await _appDbContext.Users.FirstAsync(x => x.SipUsername == model.Caller);
                    await Clients.User(user.Id.ToString()).SendAsync("answered");
                }
            }
        }

        public async Task SendIceCandidateAsync(RTCIceCandidateInit candidate) {
            _sipService.AddIceCandidate(Context.User.Identity.Name, candidate);
            await Task.CompletedTask;
        }

        public async Task<bool> GetSecureContextState() {
            var result = _sipService.GetSecureContextReady(Context.User.FindFirst<string>("SipUser"));
            return await Task.FromResult(result);
        }

        /// <summary>
        /// 处理前端挂断请求
        /// </summary>
        public async Task<bool> HangupCallAsync(string? reason = null) {
            try {
                var userName = Context.User?.Identity?.Name;
                if (string.IsNullOrEmpty(userName)) {
                    await Clients.Caller.SendAsync("hangupFailed", new { 
                        message = "用户身份验证失败", 
                        timestamp = DateTime.UtcNow 
                    });
                    return false;
                }

                // 获取用户的SIP用户名
                var user = await _appDbContext.Users.FirstOrDefaultAsync(x => x.Username == userName);
                if (user == null || string.IsNullOrEmpty(user.SipUsername)) {
                    await Clients.Caller.SendAsync("hangupFailed", new { 
                        message = "用户SIP账号信息不存在", 
                        timestamp = DateTime.UtcNow 
                    });
                    return false;
                }

                // 调用SipService的挂断方法
                var result = await _sipService.HangupWithNotificationAsync(user.SipUsername, reason);
                
                if (!result) {
                    await Clients.Caller.SendAsync("hangupFailed", new { 
                        message = "挂断操作失败", 
                        timestamp = DateTime.UtcNow 
                    });
                }

                return result;
            } catch (Exception ex) {
                await Clients.Caller.SendAsync("hangupFailed", new { 
                    message = $"挂断过程中发生错误: {ex.Message}", 
                    timestamp = DateTime.UtcNow 
                });
                return false;
            }
        }

        /// <summary>
        /// 向指定客户端发送挂断通知
        /// </summary>
        public async Task NotifyHangupAsync(string targetUserId, string reason) {
            try {
                // 验证调用者权限 - 只有系统或授权用户可以发送通知
                var currentUserName = Context.User?.Identity?.Name;
                if (string.IsNullOrEmpty(currentUserName)) {
                    return;
                }

                await Clients.User(targetUserId).SendAsync("remoteHangup", new { 
                    reason = reason, 
                    timestamp = DateTime.UtcNow,
                    initiator = currentUserName
                });
            } catch (Exception ex) {
                // 记录错误但不向客户端发送，避免暴露内部错误
                Console.WriteLine($"Error sending hangup notification: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送挂断开始通知
        /// </summary>
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

        /// <summary>
        /// 发送通话结束确认
        /// </summary>
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

        /// <summary>
        /// 发送挂断失败通知
        /// </summary>
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
    }

    public record WebRtcAnswerModel(string Caller, string AnswerSdp);
}
