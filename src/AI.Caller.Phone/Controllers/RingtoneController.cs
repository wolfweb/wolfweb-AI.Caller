using AI.Caller.Phone.Models.Dto;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RingtoneController : ControllerBase {
    private readonly IRingtoneService _ringtoneService;
    private readonly ILogger<RingtoneController> _logger;

    public RingtoneController(
        IRingtoneService ringtoneService,
        ILogger<RingtoneController> logger) {
        _ringtoneService = ringtoneService;
        _logger = logger;
    }

    private int GetCurrentUserId() {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    [HttpGet]
    public async Task<IActionResult> GetRingtones([FromQuery] string? type = null) {
        try {
            var userId = GetCurrentUserId();
            var ringtones = await _ringtoneService.GetAvailableRingtonesAsync(userId, type);
            return Ok(ringtones);
        } catch (Exception ex) {
            _logger.LogError(ex, "获取铃音列表失败");
            return StatusCode(500, "获取铃音列表失败");
        }
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadRingtone([FromForm] IFormFile file, [FromForm] UploadRingtoneDto dto) {
        try {
            if (file == null || file.Length == 0) {
                return BadRequest("请选择文件");
            }

            if (string.IsNullOrWhiteSpace(dto.Name)) {
                return BadRequest("请输入铃音名称");
            }

            if (!_ringtoneService.ValidateAudioFile(file, out string errorMessage)) {
                return BadRequest(errorMessage);
            }

            var userId = GetCurrentUserId();
            var ringtone = await _ringtoneService.UploadRingtoneAsync(file, dto.Name, dto.Type, userId);

            return Ok(new {
                id = ringtone.Id,
                name = ringtone.Name,
                filePath = ringtone.FilePath,
                message = "铃音上传成功"
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "上传铃音失败");
            return StatusCode(500, "上传铃音失败");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRingtone(int id) {
        try {
            var userId = GetCurrentUserId();
            var result = await _ringtoneService.DeleteRingtoneAsync(id, userId);

            if (!result) {
                return NotFound("铃音不存在");
            }

            return Ok("铃音删除成功");
        } catch (UnauthorizedAccessException ex) {
            return Forbid(ex.Message);
        } catch (InvalidOperationException ex) {
            return BadRequest(ex.Message);
        } catch (Exception ex) {
            _logger.LogError(ex, "删除铃音失败");
            return StatusCode(500, "删除铃音失败");
        }
    }

    [HttpGet("user-settings")]
    public async Task<IActionResult> GetUserSettings() {
        try {
            var userId = GetCurrentUserId();
            var settings = await _ringtoneService.GetUserSettingsAsync(userId);

            if (settings == null) {
                return Ok(new {
                    userId = userId,
                    incomingRingtone = (object?)null,
                    ringbackTone = (object?)null,
                    message = "用户尚未设置铃音"
                });
            }

            return Ok(settings);
        } catch (Exception ex) {
            _logger.LogError(ex, "获取用户铃音设置失败");
            return StatusCode(500, "获取用户铃音设置失败");
        }
    }

    [HttpPut("user-settings")]
    public async Task<IActionResult> UpdateUserSettings([FromBody] UpdateUserRingtoneSettingsDto dto) {
        try {
            var userId = GetCurrentUserId();
            await _ringtoneService.UpdateUserSettingsAsync(userId, dto.IncomingRingtoneId, dto.RingbackToneId);

            return Ok("铃音设置已更新");
        } catch (Exception ex) {
            _logger.LogError(ex, "更新用户铃音设置失败");
            return StatusCode(500, "更新用户铃音设置失败");
        }
    }

    [HttpGet("system-settings")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetSystemSettings() {
        try {
            var settings = await _ringtoneService.GetSystemSettingsAsync();

            if (settings == null) {
                return NotFound("系统铃音配置不存在");
            }

            return Ok(new {
                defaultIncomingRingtone = new {
                    id = settings.DefaultIncomingRingtone.Id,
                    name = settings.DefaultIncomingRingtone.Name,
                    filePath = settings.DefaultIncomingRingtone.FilePath
                },
                defaultRingbackTone = new {
                    id = settings.DefaultRingbackTone.Id,
                    name = settings.DefaultRingbackTone.Name,
                    filePath = settings.DefaultRingbackTone.FilePath
                },
                updatedAt = settings.UpdatedAt
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "获取系统铃音设置失败");
            return StatusCode(500, "获取系统铃音设置失败");
        }
    }

    [HttpPut("system-settings")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateSystemSettings([FromBody] UpdateSystemRingtoneDto dto) {
        try {
            var userId = GetCurrentUserId();
            await _ringtoneService.UpdateSystemSettingsAsync(
                dto.DefaultIncomingRingtoneId,
                dto.DefaultRingbackToneId,
                userId);

            return Ok("系统默认铃音已更新");
        } catch (Exception ex) {
            _logger.LogError(ex, "更新系统铃音设置失败");
            return StatusCode(500, "更新系统铃音设置失败");
        }
    }
}
