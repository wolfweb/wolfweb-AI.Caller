using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AI.Caller.Phone.Services;
using AI.Caller.Phone.Models;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SimpleRecordingController : ControllerBase
    {
        private readonly ISimpleRecordingService _recordingService;
        private readonly ILogger<SimpleRecordingController> _logger;

        public SimpleRecordingController(
            ISimpleRecordingService recordingService,
            ILogger<SimpleRecordingController> logger)
        {
            _recordingService = recordingService;
            _logger = logger;
        }

        /// <summary>
        /// 开始录音
        /// </summary>
        [HttpPost("start")]
        public async Task<IActionResult> StartRecording([FromBody] StartRecordingRequest request)
        {
            try
            {
                var result = await _recordingService.StartRecordingAsync(request.SipUsername);
                if (result)
                {
                    return Ok(new { success = true, message = "录音已开始" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "开始录音失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "开始录音时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 停止录音
        /// </summary>
        [HttpPost("stop")]
        public async Task<IActionResult> StopRecording([FromBody] StopRecordingRequest request)
        {
            try
            {
                var result = await _recordingService.StopRecordingAsync(request.SipUsername);
                if (result)
                {
                    return Ok(new { success = true, message = "录音已停止" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "停止录音失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止录音时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取录音列表
        /// </summary>
        [HttpGet("list")]
        public async Task<IActionResult> GetRecordings()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized(new { success = false, message = "用户身份验证失败" });
                }

                var recordings = await _recordingService.GetRecordingsAsync(userId);
                return Ok(new { success = true, data = recordings });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取录音列表时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 删除录音
        /// </summary>
        [HttpDelete("{recordingId}")]
        public async Task<IActionResult> DeleteRecording(int recordingId)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized(new { success = false, message = "用户身份验证失败" });
                }

                var result = await _recordingService.DeleteRecordingAsync(recordingId, userId);
                if (result)
                {
                    return Ok(new { success = true, message = "录音已删除" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "删除录音失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除录音时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取自动录音设置
        /// </summary>
        [HttpGet("auto-recording")]
        public async Task<IActionResult> GetAutoRecordingSetting()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized(new { success = false, message = "用户身份验证失败" });
                }

                var enabled = await _recordingService.IsAutoRecordingEnabledAsync(userId);
                return Ok(new { success = true, enabled = enabled });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取自动录音设置时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 设置自动录音
        /// </summary>
        [HttpPost("auto-recording")]
        public async Task<IActionResult> SetAutoRecording([FromBody] SetAutoRecordingRequest request)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized(new { success = false, message = "用户身份验证失败" });
                }

                var result = await _recordingService.SetAutoRecordingAsync(userId, request.Enabled);
                if (result)
                {
                    return Ok(new { success = true, message = "自动录音设置已更新" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "设置自动录音失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置自动录音时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取录音状态
        /// </summary>
        [HttpGet("status/{sipUsername}")]
        public async Task<IActionResult> GetRecordingStatus(string sipUsername)
        {
            try
            {
                var status = await _recordingService.GetRecordingStatusAsync(sipUsername);
                return Ok(new { success = true, status = status?.ToString() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取录音状态时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }
    }

    // 请求模型
    public class StartRecordingRequest
    {
        public string SipUsername { get; set; } = string.Empty;
    }

    public class StopRecordingRequest
    {
        public string SipUsername { get; set; } = string.Empty;
    }

    public class SetAutoRecordingRequest
    {
        public bool Enabled { get; set; }
    }
}