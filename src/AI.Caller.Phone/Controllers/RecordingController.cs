using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AI.Caller.Phone.Services;

using System.Security.Claims;

namespace AI.Caller.Phone.Controllers
{
    [Authorize]
    public class RecordingController : Controller
    {
        private readonly ISimpleRecordingService _recordingService;
        private readonly ILogger<RecordingController> _logger;

        public RecordingController(
            ISimpleRecordingService recordingService,
            ILogger<RecordingController> logger)
        {
            _recordingService = recordingService;
            _logger = logger;
        }

        /// <summary>
        /// 录音管理主页面
        /// </summary>
        public async Task<IActionResult> Index()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var userId))
                {
                    return RedirectToAction("Login", "Account");
                }

                // 获取录音列表
                var recordings = await _recordingService.GetRecordingsAsync(userId);
                
                // 获取自动录音设置
                var autoRecordingEnabled = await _recordingService.IsAutoRecordingEnabledAsync(userId);

                ViewBag.AutoRecordingEnabled = autoRecordingEnabled;
                
                return View("Simple", recordings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载录音管理页面时发生错误");
                TempData["ErrorMessage"] = "加载录音列表失败，请稍后重试";
                return View("Simple", new List<AI.Caller.Phone.Models.Recording>());
            }
        }



        /// <summary>
        /// 录音功能测试页面
        /// </summary>
        public IActionResult Test()
        {
            return View();
        }

        /// <summary>
        /// 录音设置页面
        /// </summary>
        public async Task<IActionResult> Settings()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var userId))
                {
                    return RedirectToAction("Login", "Account");
                }

                var autoRecordingEnabled = await _recordingService.IsAutoRecordingEnabledAsync(userId);
                ViewBag.AutoRecordingEnabled = autoRecordingEnabled;
                
                return View("SimpleSettings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载录音设置页面时发生错误");
                TempData["ErrorMessage"] = "加载设置失败，请稍后重试";
                return View("SimpleSettings");
            }
        }

        /// <summary>
        /// 更新录音设置
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UpdateSettings(bool autoRecordingEnabled, string recordingQuality = "medium", string recordingFormat = "mp3")
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var userId))
                {
                    return Json(new { success = false, message = "用户身份验证失败" });
                }

                var result = await _recordingService.SetAutoRecordingAsync(userId, autoRecordingEnabled);
                
                if (result)
                {
                    TempData["SuccessMessage"] = "录音设置已更新";
                    return Json(new { success = true, message = "设置已保存" });
                }
                else
                {
                    return Json(new { success = false, message = "保存设置失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新录音设置时发生错误");
                return Json(new { success = false, message = "服务器错误，请稍后重试" });
            }
        }

        /// <summary>
        /// 删除录音文件
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteRecording(int recordingId)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var userId))
                {
                    return Json(new { success = false, message = "用户身份验证失败" });
                }

                var result = await _recordingService.DeleteRecordingAsync(recordingId, userId);
                
                if (result)
                {
                    return Json(new { success = true, message = "录音文件已删除" });
                }
                else
                {
                    return Json(new { success = false, message = "删除失败，文件可能不存在" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除录音文件时发生错误，录音ID: {RecordingId}", recordingId);
                return Json(new { success = false, message = "删除失败，请稍后重试" });
            }
        }

        /// <summary>
        /// 批量删除录音文件
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> BatchDeleteRecordings([FromBody] int[] recordingIds)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var userId))
                {
                    return Json(new { success = false, message = "用户身份验证失败" });
                }

                int successCount = 0;
                int failCount = 0;

                foreach (var recordingId in recordingIds)
                {
                    try
                    {
                        var result = await _recordingService.DeleteRecordingAsync(recordingId, userId);
                        if (result)
                            successCount++;
                        else
                            failCount++;
                    }
                    catch
                    {
                        failCount++;
                    }
                }

                return Json(new { 
                    success = true, 
                    message = $"删除完成：成功 {successCount} 个，失败 {failCount} 个",
                    successCount = successCount,
                    failCount = failCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除录音文件时发生错误");
                return Json(new { success = false, message = "批量删除失败，请稍后重试" });
            }
        }

        /// <summary>
        /// 获取录音文件详情
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRecordingDetails(int recordingId)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var userId))
                {
                    return Json(new { success = false, message = "用户身份验证失败" });
                }

                // 这里需要实现获取录音详情的服务方法
                // var recording = await _recordingService.GetRecordingDetailsAsync(recordingId, userId);
                
                return Json(new { 
                    success = true, 
                    data = new { 
                        id = recordingId,
                        fileName = "recording_" + recordingId + ".mp3",
                        duration = "00:05:30",
                        fileSize = "2.5 MB",
                        quality = "128kbps",
                        format = "MP3"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取录音详情时发生错误，录音ID: {RecordingId}", recordingId);
                return Json(new { success = false, message = "获取录音详情失败" });
            }
        }
    }
}