using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIPSorcery.Net;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PhoneController : ControllerBase
    {
        private readonly SipService _sipService;
        private readonly ILogger<PhoneController> _logger;
        private readonly ApplicationContext _applicationContext;

        public PhoneController(ILogger<PhoneController> logger, SipService sipService, ApplicationContext applicationContext)
        {
            _logger             = logger;
            _sipService         = sipService;
            _applicationContext = applicationContext;
        }

        [HttpPost("call")]
        public async Task<IActionResult> Call([FromBody] CallRequest request)
        {
            try
            {
                var username = User.FindFirst<string>("SipUser");
                _logger.LogInformation($"用户 {username} 正在呼叫 {request.Destination}");

                // 使用SipService发起呼叫
                var result = await _sipService.MakeCallAsync($"sip:{request.Destination}@{_applicationContext.SipServer}", username, request.Offer);
                
                if (!result.Success)
                {
                    return BadRequest(new { error = result.Message });
                }
                
                // 返回WebRTC offer
                var peerConnection = result.Data;
                if (peerConnection == null)
                {
                    return StatusCode(500, new { error = "无法创建WebRTC连接" });
                }
                
                // 确保返回标准格式的SDP offer
                var localDescription = peerConnection.localDescription;
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "呼叫失败");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("hangup")]
        public async Task<IActionResult> Hangup()
        {
            try
            {
                var username = User.FindFirst<string>("SipUser");
                _logger.LogInformation($"用户 {username} 请求挂断电话");

                // 使用SipService挂断电话
                var result = await _sipService.HangupCallAsync(username);
                
                if (!result)
                {
                    _logger.LogWarning($"用户 {username} 挂断电话失败: SipService返回失败");
                    return BadRequest(new { error = "挂断电话失败，可能没有活动的通话" });
                }
                
                _logger.LogInformation($"用户 {username} 挂断电话成功");
                return Ok(new { success = true, message = "通话已结束" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "挂断电话失败");
                return StatusCode(500, new { error = $"挂断电话时发生错误: {ex.Message}" });
            }
        }

        [HttpPost("dtmf")]
        public async Task<IActionResult> SendDtmf([FromBody] DtmfRequest request)
        {
            try
            {
                var username = User.FindFirst<string>("SipUser");
                // 使用SipService发送DTMF音调
                var result = await _sipService.SendDtmfAsync(username, request.Tone);
                
                if (!result)
                {
                    return BadRequest(new { error = "发送DTMF失败" });
                }
                
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送DTMF失败");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class CallRequest
    {
        public string                    Destination { get; set; }
        public RTCSessionDescriptionInit Offer       { get; set; }
    }

    public class DtmfRequest
    {
        public byte Tone { get; set; }
    }
}