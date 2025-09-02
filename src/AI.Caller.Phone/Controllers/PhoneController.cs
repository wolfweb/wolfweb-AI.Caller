using AI.Caller.Phone;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.Net;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Controllers {
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PhoneController : ControllerBase {
        private readonly SipService _sipService;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<PhoneController> _logger;
        private readonly ApplicationContext _applicationContext;

        public PhoneController(ILogger<PhoneController> logger, SipService sipService, AppDbContext dbContext, ApplicationContext applicationContext) {
            _logger = logger;
            _sipService = sipService;
            _dbContext = dbContext;
            _applicationContext = applicationContext;
        }

        [HttpPost("call")]
        public async Task<IActionResult> Call([FromBody] CallRequest request) {
            try {
                var userId = User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                _logger.LogInformation($"用户 {userId} 正在呼叫 {request.Destination}");
                var caller = await _dbContext.Users.Include(u => u.SipAccount).FirstAsync(x => x.Id == userId);
                var callee = await _dbContext.Users.Include(u => u.SipAccount).FirstOrDefaultAsync(x => x.SipAccount!=null && x.SipAccount.SipUsername == request.Destination);

                var sipServer = callee != null ? callee.SipAccount!.SipServer : caller.SipAccount!.SipServer;

                var result = await _sipService.MakeCallAsync($"sip:{request.Destination}@{sipServer}", caller, request.Offer);

                if (!result.Success) {
                    return BadRequest(new { error = result.Message });
                }

                var response = new { 
                    success = true, 
                    message = "呼叫已发起",
                    callContext = new {
                        callId = Guid.NewGuid().ToString(),
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
        public async Task<IActionResult> Hangup() {
            try {
                var userId = User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                var user = await _dbContext.Users
                    .Include(u => u.SipAccount)
                    .FirstAsync(x => x.Id == userId);
                _logger.LogInformation($"用户 {user.Username} 请求挂断电话");

                var result = await _sipService.HangupCallAsync(user);

                if (!result) {
                    _logger.LogWarning($"用户 {user.Username} 挂断电话失败: SipService返回失败");
                    return BadRequest(new { error = "挂断电话失败，可能没有活动的通话" });
                }

                _logger.LogInformation($"用户 {user.Username} 挂断电话成功");
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
                var user = await _dbContext.Users
                    .Include(u => u.SipAccount)
                    .FirstAsync(x => x.Id == userId);
                var result = await _sipService.SendDtmfAsync(request.Tone, user);

                if (!result) {
                    return BadRequest(new { error = "发送DTMF失败" });
                }

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
        public byte Tone { get; set; }
    }
}