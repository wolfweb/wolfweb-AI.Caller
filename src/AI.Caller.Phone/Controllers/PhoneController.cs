using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.Net;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers {
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PhoneController : ControllerBase {
        private readonly ILogger _logger;
        private readonly AppDbContext _dbContext;
        private readonly ICallManager _callManager;

        public PhoneController(
            ILogger<PhoneController> logger, 
            AppDbContext dbContext,
            ICallManager callManager) {
            _logger      = logger;
            _dbContext   = dbContext;
            _callManager = callManager;
        }

        [HttpPost("call")]
        public async Task<IActionResult> Call([FromBody] CallRequest request) {
            try {
                var userId = User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                _logger.LogInformation($"用户 {userId} 正在呼叫 {request.Destination}");
                var caller = await _dbContext.Users.Include(u => u.SipAccount).FirstAsync(x => x.Id == userId);
                var callee = await _dbContext.Users.Include(u => u.SipAccount).FirstOrDefaultAsync(x => x.SipAccount!=null && x.SipAccount.SipUsername == request.Destination);

                CallScenario scenario = CallScenario.WebToWeb;

                string sipServer = caller.SipAccount!.SipServer;
                if(callee != null) {
                    sipServer = callee.SipAccount!.SipServer;
                } else {
                    scenario = CallScenario.WebToMobile;
                }

                var ctx = await _callManager.MakeCallAsync($"sip:{request.Destination}@{sipServer}", caller, request.Offer, scenario);

                var response = new { 
                    success = true, 
                    message = "呼叫已发起",
                    callContext = new {
                        callId = ctx.CallId,
                        caller = new {
                            userId = caller.Id,
                            sipUsername = caller.SipAccount?.SipUsername
                        },
                        callee = new {
                            userId = callee?.Id,
                            sipUsername = request.Destination
                        },
                        isExternal = callee == null,
                        timestamp = DateTime.UtcNow
                    }
                };

                return Ok(response);
            } catch (Exception ex) {
                _logger.LogError(ex, "呼叫失败");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("hangup")]
        public async Task<IActionResult> Hangup(string callId) {
            try {
                var userId = User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                _logger.LogInformation($"用户 {userId} 请求挂断电话");
                await _callManager.HangupCallAsync(callId, userId);
                _logger.LogInformation($"用户 {userId} 挂断电话成功");
                return Ok(new { success = true, message = "通话已结束" });
            } catch (Exception ex) {
                _logger.LogError(ex, "挂断电话失败");
                return StatusCode(500, new { error = $"挂断电话时发生错误: {ex.Message}" });
            }
        }

        [HttpPost("dtmf")]
        public async Task<IActionResult> SendDtmf([FromBody] DtmfRequest request) {
            try {
                var userId = User!.FindFirst<int>(ClaimTypes.NameIdentifier);                
                await _callManager.SendDtmfAsync(request.Tone, userId, request.CallId);
                return Ok();
            } catch (Exception ex) {
                _logger.LogError(ex, "发送DTMF失败");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class CallRequest {
        public string Destination { get; set; }
        public RTCSessionDescriptionInit Offer { get; set; }
    }

    public class DtmfRequest {
        public string CallId { get; set; }
        public byte Tone { get; set; }
    }
}