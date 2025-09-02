using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AI.Caller.Phone.Services;
using AI.Caller.Phone.Models;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers {
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SimpleRecordingController : ControllerBase {
        private readonly ISimpleRecordingService _recordingService;
        private readonly ILogger<SimpleRecordingController> _logger;

        public SimpleRecordingController(
            ISimpleRecordingService recordingService,
            ILogger<SimpleRecordingController> logger) {
            _recordingService = recordingService;
            _logger = logger;
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartRecording() {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var result = await _recordingService.StartRecordingAsync(userId);
                if (result) {
                    return Ok(new { success = true, message = "录音已开始" });
                } else {
                    return BadRequest(new { success = false, message = "开始录音失败" });
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "开始录音时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("stop")]
        public async Task<IActionResult> StopRecording() {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var result = await _recordingService.StopRecordingAsync(userId);
                if (result) {
                    return Ok(new { success = true, message = "录音已停止" });
                } else {
                    return BadRequest(new { success = false, message = "停止录音失败" });
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "停止录音时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetRecordings() {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var recordings = await _recordingService.GetRecordingsAsync(userId);
                return Ok(new { success = true, data = recordings });
            } catch (Exception ex) {
                _logger.LogError(ex, "获取录音列表时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpDelete("{recordingId}")]
        public async Task<IActionResult> DeleteRecording(int recordingId) {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var result = await _recordingService.DeleteRecordingAsync(recordingId, userId);
                if (result) {
                    return Ok(new { success = true, message = "录音已删除" });
                } else {
                    return BadRequest(new { success = false, message = "删除录音失败" });
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "删除录音时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("auto-recording")]
        public async Task<IActionResult> GetAutoRecordingSetting() {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var enabled = await _recordingService.IsAutoRecordingEnabledAsync(userId);
                return Ok(new { success = true, enabled = enabled });
            } catch (Exception ex) {
                _logger.LogError(ex, "获取自动录音设置时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("auto-recording")]
        public async Task<IActionResult> SetAutoRecording([FromBody] SetAutoRecordingRequest request) {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var result = await _recordingService.SetAutoRecordingAsync(userId, request.Enabled);
                if (result) {
                    return Ok(new { success = true, message = "自动录音设置已更新" });
                } else {
                    return BadRequest(new { success = false, message = "设置自动录音失败" });
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "设置自动录音时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("status/{userId}")]
        public async Task<IActionResult> GetRecordingStatus(int userId) {
            try {
                var status = await _recordingService.GetRecordingStatusAsync(userId);
                return Ok(new { success = true, status = status?.ToString() });
            } catch (Exception ex) {
                _logger.LogError(ex, "获取录音状态时发生错误");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }
    }

    public class SetAutoRecordingRequest {
        public bool Enabled { get; set; }
    }
}
