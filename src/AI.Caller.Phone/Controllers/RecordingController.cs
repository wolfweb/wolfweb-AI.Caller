using AI.Caller.Phone.Services;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers;

[Authorize]
public class RecordingController : Controller
{
    private readonly IRecordingService _recordingService;
    private readonly ILogger<RecordingController> _logger;

    public RecordingController(IRecordingService recordingService, ILogger<RecordingController> logger)
    {
        _recordingService = recordingService;
        _logger = logger;
    }

    /// <summary>
    /// 录音列表页面
    /// </summary>
    public async Task<IActionResult> Index(RecordingFilter? filter = null)
    {
        var userId = GetCurrentUserId();
        filter ??= new RecordingFilter();
        
        var recordings = await _recordingService.GetRecordingsAsync(userId, filter);
        var storageInfo = await _recordingService.GetStorageInfoAsync();
        
        ViewBag.StorageInfo = storageInfo;
        ViewBag.Filter = filter;
        
        return View(recordings);
    }

    /// <summary>
    /// 获取录音列表 API
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRecordings([FromQuery] RecordingFilter filter)
    {
        try
        {
            var userId = GetCurrentUserId();
            var recordings = await _recordingService.GetRecordingsAsync(userId, filter);
            return Json(recordings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取录音列表失败");
            return BadRequest(new { message = "获取录音列表失败" });
        }
    }

    /// <summary>
    /// 下载录音文件
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Download(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            
            if (!await _recordingService.HasRecordingPermissionAsync(userId, id))
                return Forbid();

            var recording = await _recordingService.GetRecordingAsync(id);
            if (recording == null)
                return NotFound();

            var stream = await _recordingService.GetRecordingStreamAsync(id);
            if (stream == null)
                return NotFound();

            var fileName = $"recording_{recording.StartTime:yyyyMMdd_HHmmss}_{recording.CallerNumber}_{recording.CalleeNumber}.{recording.AudioFormat}";
            return File(stream, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"下载录音失败 - RecordingId: {id}");
            return BadRequest(new { message = "下载录音失败" });
        }
    }

    /// <summary>
    /// 删除录音
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var success = await _recordingService.DeleteRecordingAsync(id, userId);
            
            if (success)
                return Ok(new { message = "录音删除成功" });
            else
                return BadRequest(new { message = "录音删除失败" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"删除录音失败 - RecordingId: {id}");
            return BadRequest(new { message = "删除录音失败" });
        }
    }

    /// <summary>
    /// 获取存储信息
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStorageInfo()
    {
        try
        {
            var storageInfo = await _recordingService.GetStorageInfoAsync();
            return Json(storageInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取存储信息失败");
            return BadRequest(new { message = "获取存储信息失败" });
        }
    }

    /// <summary>
    /// 播放录音 (流式传输)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Play(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            
            if (!await _recordingService.HasRecordingPermissionAsync(userId, id))
                return Forbid();

            var recording = await _recordingService.GetRecordingAsync(id);
            if (recording == null)
                return NotFound();

            var stream = await _recordingService.GetRecordingStreamAsync(id);
            if (stream == null)
                return NotFound();

            return File(stream, $"audio/{recording.AudioFormat}", enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"播放录音失败 - RecordingId: {id}");
            return BadRequest();
        }
    }

    /// <summary>
    /// 录音设置页面
    /// </summary>
    public async Task<IActionResult> Settings()
    {
        try
        {
            var userId = GetCurrentUserId();
            var settings = await GetUserRecordingSettingsAsync(userId);
            return View(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取录音设置失败");
            return View(new RecordingSetting { UserId = GetCurrentUserId() });
        }
    }

    /// <summary>
    /// 保存录音设置
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SaveSettings(RecordingSetting settings)
    {
        try
        {
            var userId = GetCurrentUserId();
            settings.UserId = userId;
            
            var success = await SaveUserRecordingSettingsAsync(settings);
            if (success)
            {
                TempData["SuccessMessage"] = "录音设置保存成功";
                return RedirectToAction("Settings");
            }
            else
            {
                ModelState.AddModelError("", "保存录音设置失败");
                return View("Settings", settings);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存录音设置失败");
            ModelState.AddModelError("", "保存录音设置失败");
            return View("Settings", settings);
        }
    }

    private async Task<RecordingSetting> GetUserRecordingSettingsAsync(int userId)
    {
        // 这里应该调用RecordingService的方法，但目前RecordingService中是私有方法
        // 暂时返回默认设置
        return new RecordingSetting
        {
            UserId = userId,
            AutoRecording = true,
            StoragePath = "recordings",
            MaxRetentionDays = 30,
            MaxStorageSizeMB = 1024,
            AudioFormat = "wav",
            AudioQuality = 44100,
            EnableCompression = true
        };
    }

    private async Task<bool> SaveUserRecordingSettingsAsync(RecordingSetting settings)
    {
        // 这里应该调用RecordingService的方法来保存设置
        // 暂时返回true
        return true;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}