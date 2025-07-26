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
        private readonly IRecordingService? _recordingService;
        
        public WebRtcHub(SipService sipService, AppDbContext appDbContext, IRecordingService? recordingService = null) {
            _sipService = sipService;
            _appDbContext = appDbContext;
            _recordingService = recordingService;
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

        #region 录音功能 SignalR 方法

        /// <summary>
        /// 开始录音
        /// </summary>
        /// <param name="calleeNumber">被叫号码</param>
        /// <returns>录音操作结果</returns>
        public async Task<RecordingResult> StartRecordingAsync(string calleeNumber)
        {
            try
            {
                var userName = Context.User?.Identity?.Name;
                if (string.IsNullOrEmpty(userName))
                {
                    var errorResult = new RecordingResult
                    {
                        Success = false,
                        Message = "用户身份验证失败"
                    };

                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = errorResult.Message,
                        timestamp = DateTime.UtcNow
                    });

                    return errorResult;
                }

                // 获取用户的SIP用户名
                var user = await _appDbContext.Users.FirstOrDefaultAsync(x => x.Username == userName);
                if (user == null || string.IsNullOrEmpty(user.SipUsername))
                {
                    var errorResult = new RecordingResult
                    {
                        Success = false,
                        Message = "用户SIP账号信息不存在"
                    };

                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = errorResult.Message,
                        timestamp = DateTime.UtcNow
                    });

                    return errorResult;
                }

                // 调用SipService的录音方法
                var result = await _sipService.StartRecordingAsync(user.SipUsername, calleeNumber);

                // 发送录音状态通知
                if (result.Success)
                {
                    await Clients.Caller.SendAsync("recordingStarted", new
                    {
                        callId = result.CallId,
                        recordingId = result.RecordingId,
                        message = result.Message,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = result.Message,
                        timestamp = DateTime.UtcNow
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                var errorResult = new RecordingResult
                {
                    Success = false,
                    Message = $"开始录音时发生错误: {ex.Message}"
                };

                await Clients.Caller.SendAsync("recordingError", new
                {
                    message = errorResult.Message,
                    timestamp = DateTime.UtcNow
                });

                return errorResult;
            }
        }

        /// <summary>
        /// 停止录音
        /// </summary>
        /// <returns>录音操作结果</returns>
        public async Task<RecordingResult> StopRecordingAsync()
        {
            try
            {
                var userName = Context.User?.Identity?.Name;
                if (string.IsNullOrEmpty(userName))
                {
                    var errorResult = new RecordingResult
                    {
                        Success = false,
                        Message = "用户身份验证失败"
                    };

                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = errorResult.Message,
                        timestamp = DateTime.UtcNow
                    });

                    return errorResult;
                }

                // 获取用户的SIP用户名
                var user = await _appDbContext.Users.FirstOrDefaultAsync(x => x.Username == userName);
                if (user == null || string.IsNullOrEmpty(user.SipUsername))
                {
                    var errorResult = new RecordingResult
                    {
                        Success = false,
                        Message = "用户SIP账号信息不存在"
                    };

                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = errorResult.Message,
                        timestamp = DateTime.UtcNow
                    });

                    return errorResult;
                }

                // 调用SipService的停止录音方法
                var result = await _sipService.StopRecordingAsync(user.SipUsername);

                // 发送录音状态通知
                if (result.Success)
                {
                    await Clients.Caller.SendAsync("recordingStopped", new
                    {
                        callId = result.CallId,
                        recordingId = result.RecordingId,
                        message = result.Message,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = result.Message,
                        timestamp = DateTime.UtcNow
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                var errorResult = new RecordingResult
                {
                    Success = false,
                    Message = $"停止录音时发生错误: {ex.Message}"
                };

                await Clients.Caller.SendAsync("recordingError", new
                {
                    message = errorResult.Message,
                    timestamp = DateTime.UtcNow
                });

                return errorResult;
            }
        }

        /// <summary>
        /// 获取录音状态
        /// </summary>
        /// <returns>录音状态</returns>
        public async Task<RecordingStatus?> GetRecordingStatusAsync()
        {
            try
            {
                var userName = Context.User?.Identity?.Name;
                if (string.IsNullOrEmpty(userName))
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = "用户身份验证失败",
                        timestamp = DateTime.UtcNow
                    });
                    return null;
                }

                // 获取用户的SIP用户名
                var user = await _appDbContext.Users.FirstOrDefaultAsync(x => x.Username == userName);
                if (user == null || string.IsNullOrEmpty(user.SipUsername))
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = "用户SIP账号信息不存在",
                        timestamp = DateTime.UtcNow
                    });
                    return null;
                }

                // 获取录音状态
                var status = await _sipService.GetRecordingStatusAsync(user.SipUsername);

                // 发送状态更新通知
                await Clients.Caller.SendAsync("recordingStatusUpdate", new
                {
                    status = status?.ToString(),
                    timestamp = DateTime.UtcNow
                });

                return status;
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("recordingError", new
                {
                    message = $"获取录音状态时发生错误: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
                return null;
            }
        }

        /// <summary>
        /// 获取用户录音列表
        /// </summary>
        /// <param name="page">页码</param>
        /// <param name="pageSize">页面大小</param>
        /// <returns>录音列表</returns>
        public async Task<object> GetRecordingListAsync(int page = 1, int pageSize = 10)
        {
            try
            {
                if (_recordingService == null)
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = "录音服务未启用",
                        timestamp = DateTime.UtcNow
                    });
                    return new { success = false, message = "录音服务未启用" };
                }

                var userName = Context.User?.Identity?.Name;
                if (string.IsNullOrEmpty(userName))
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = "用户身份验证失败",
                        timestamp = DateTime.UtcNow
                    });
                    return new { success = false, message = "用户身份验证失败" };
                }

                // 获取用户信息
                var user = await _appDbContext.Users.FirstOrDefaultAsync(x => x.Username == userName);
                if (user == null)
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = "用户不存在",
                        timestamp = DateTime.UtcNow
                    });
                    return new { success = false, message = "用户不存在" };
                }

                // 获取录音列表
                var filter = new RecordingFilter
                {
                    Page = page,
                    PageSize = pageSize
                };

                var recordings = await _recordingService.GetRecordingsAsync(user.Id, filter);

                // 转换为前端友好的格式
                var result = new
                {
                    success = true,
                    data = recordings.Items.Select(r => new
                    {
                        id = r.Id,
                        callId = r.CallId,
                        callerNumber = r.CallerNumber,
                        calleeNumber = r.CalleeNumber,
                        startTime = r.StartTime,
                        endTime = r.EndTime,
                        duration = r.Duration.TotalSeconds,
                        fileSize = r.FileSize,
                        status = r.Status.ToString(),
                        createdAt = r.CreatedAt
                    }),
                    totalCount = recordings.TotalCount,
                    page = recordings.Page,
                    pageSize = recordings.PageSize,
                    totalPages = recordings.TotalPages
                };

                // 发送录音列表更新通知
                await Clients.Caller.SendAsync("recordingListUpdate", result);

                return result;
            }
            catch (Exception ex)
            {
                var errorResult = new
                {
                    success = false,
                    message = $"获取录音列表时发生错误: {ex.Message}"
                };

                await Clients.Caller.SendAsync("recordingError", new
                {
                    message = errorResult.message,
                    timestamp = DateTime.UtcNow
                });

                return errorResult;
            }
        }

        /// <summary>
        /// 删除录音记录
        /// </summary>
        /// <param name="recordingId">录音ID</param>
        /// <returns>删除结果</returns>
        public async Task<object> DeleteRecordingAsync(int recordingId)
        {
            try
            {
                if (_recordingService == null)
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = "录音服务未启用",
                        timestamp = DateTime.UtcNow
                    });
                    return new { success = false, message = "录音服务未启用" };
                }

                var userName = Context.User?.Identity?.Name;
                if (string.IsNullOrEmpty(userName))
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = "用户身份验证失败",
                        timestamp = DateTime.UtcNow
                    });
                    return new { success = false, message = "用户身份验证失败" };
                }

                // 获取用户信息
                var user = await _appDbContext.Users.FirstOrDefaultAsync(x => x.Username == userName);
                if (user == null)
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = "用户不存在",
                        timestamp = DateTime.UtcNow
                    });
                    return new { success = false, message = "用户不存在" };
                }

                // 删除录音记录
                var success = await _recordingService.DeleteRecordingAsync(recordingId, user.Id);

                var result = new
                {
                    success = success,
                    message = success ? "录音删除成功" : "录音删除失败",
                    recordingId = recordingId
                };

                // 发送删除结果通知
                if (success)
                {
                    await Clients.Caller.SendAsync("recordingDeleted", new
                    {
                        recordingId = recordingId,
                        message = result.message,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = result.message,
                        timestamp = DateTime.UtcNow
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                var errorResult = new
                {
                    success = false,
                    message = $"删除录音时发生错误: {ex.Message}"
                };

                await Clients.Caller.SendAsync("recordingError", new
                {
                    message = errorResult.message,
                    timestamp = DateTime.UtcNow
                });

                return errorResult;
            }
        }

        /// <summary>
        /// 获取存储信息
        /// </summary>
        /// <returns>存储使用情况</returns>
        public async Task<object> GetStorageInfoAsync()
        {
            try
            {
                if (_recordingService == null)
                {
                    await Clients.Caller.SendAsync("recordingError", new
                    {
                        message = "录音服务未启用",
                        timestamp = DateTime.UtcNow
                    });
                    return new { success = false, message = "录音服务未启用" };
                }

                // 获取存储信息
                var storageInfo = await _recordingService.GetStorageInfoAsync();

                var result = new
                {
                    success = true,
                    data = new
                    {
                        totalSizeMB = storageInfo.TotalSizeMB,
                        usedSizeMB = storageInfo.UsedSizeMB,
                        availableSizeMB = storageInfo.AvailableSizeMB,
                        recordingCount = storageInfo.RecordingCount,
                        usagePercentage = storageInfo.UsagePercentage
                    }
                };

                // 发送存储信息更新通知
                await Clients.Caller.SendAsync("storageInfoUpdate", result);

                return result;
            }
            catch (Exception ex)
            {
                var errorResult = new
                {
                    success = false,
                    message = $"获取存储信息时发生错误: {ex.Message}"
                };

                await Clients.Caller.SendAsync("recordingError", new
                {
                    message = errorResult.message,
                    timestamp = DateTime.UtcNow
                });

                return errorResult;
            }
        }

        #endregion
    }

    public record WebRtcAnswerModel(string Caller, string AnswerSdp);
}
