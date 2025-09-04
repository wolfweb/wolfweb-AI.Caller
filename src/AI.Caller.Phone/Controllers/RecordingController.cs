using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AI.Caller.Phone.Services;

using System.Security.Claims;

namespace AI.Caller.Phone.Controllers {
    [Authorize]
    public class RecordingController : Controller {
        private readonly ISimpleRecordingService _recordingService;
        private readonly ILogger<RecordingController> _logger;

        public RecordingController(
            ISimpleRecordingService recordingService,
            ILogger<RecordingController> logger) {
            _recordingService = recordingService;
            _logger = logger;
        }

        /// <summary>
        /// 录音管理主页面
        /// </summary>
        public async Task<IActionResult> Index() {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);                
                var isAdmin = User.HasClaim("isAdmin", "True");
                
                List<AI.Caller.Phone.Models.Recording> recordings;
                if (isAdmin) {
                    recordings = await _recordingService.GetAllRecordingsAsync();
                } else {
                    recordings = await _recordingService.GetRecordingsAsync(userId);
                }

                var autoRecordingEnabled = await _recordingService.IsAutoRecordingEnabledAsync(userId);

                ViewBag.AutoRecordingEnabled = autoRecordingEnabled;
                ViewBag.IsAdmin = isAdmin;

                return View("Simple", recordings);
            } catch (Exception ex) {
                _logger.LogError(ex, "加载录音管理页面时发生错误");
                TempData["ErrorMessage"] = "加载录音列表失败，请稍后重试";
                return View("Simple", new List<AI.Caller.Phone.Models.Recording>());
            }
        }



        /// <summary>
        /// 录音功能测试页面
        /// </summary>
        public IActionResult Test() {
            return View();
        }

        /// <summary>
        /// 录音设置页面
        /// </summary>
        public async Task<IActionResult> Settings() {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);

                var autoRecordingEnabled = await _recordingService.IsAutoRecordingEnabledAsync(userId);
                ViewBag.AutoRecordingEnabled = autoRecordingEnabled;

                return View("SimpleSettings");
            } catch (Exception ex) {
                _logger.LogError(ex, "加载录音设置页面时发生错误");
                TempData["ErrorMessage"] = "加载设置失败，请稍后重试";
                return View("SimpleSettings");
            }
        }

        /// <summary>
        /// 更新录音设置
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UpdateSettings(bool autoRecordingEnabled, string recordingQuality = "medium", string recordingFormat = "mp3") {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);

                var result = await _recordingService.SetAutoRecordingAsync(userId, autoRecordingEnabled);

                if (result) {
                    TempData["SuccessMessage"] = "录音设置已更新";
                    return Json(new { success = true, message = "设置已保存" });
                } else {
                    return Json(new { success = false, message = "保存设置失败" });
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "更新录音设置时发生错误");
                return Json(new { success = false, message = "服务器错误，请稍后重试" });
            }
        }

        /// <summary>
        /// 删除录音文件
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteRecording(int recordingId) {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var isAdmin = User.HasClaim("isAdmin", "True");

                var result = await _recordingService.DeleteRecordingAsync(recordingId, isAdmin ? (int?)null : userId);

                if (result) {
                    return Json(new { success = true, message = "录音文件已删除" });
                } else {
                    return Json(new { success = false, message = "删除失败，文件可能不存在" });
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "删除录音文件时发生错误，录音ID: {RecordingId}", recordingId);
                return Json(new { success = false, message = "删除失败，请稍后重试" });
            }
        }

        /// <summary>
        /// 批量删除录音文件
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> BatchDeleteRecordings([FromBody] int[] recordingIds) {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var isAdmin = User.HasClaim("isAdmin", "True");

                int successCount = 0;
                int failCount = 0;

                foreach (var recordingId in recordingIds) {
                    try {
                        var result = await _recordingService.DeleteRecordingAsync(recordingId, isAdmin ? (int?)null : userId);
                        if (result)
                            successCount++;
                        else
                            failCount++;
                    } catch {
                        failCount++;
                    }
                }

                return Json(new {
                    success = true,
                    message = $"删除完成：成功 {successCount} 个，失败 {failCount} 个",
                    successCount = successCount,
                    failCount = failCount
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "批量删除录音文件时发生错误");
                return Json(new { success = false, message = "批量删除失败，请稍后重试" });
            }
        }

        /// <summary>
        /// 获取录音文件详情
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRecordingDetails(int recordingId) {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var isAdmin = User.HasClaim("isAdmin", "True");

                List<AI.Caller.Phone.Models.Recording> recordings;
                if (isAdmin) {
                    recordings = await _recordingService.GetAllRecordingsAsync();
                } else {
                    recordings = await _recordingService.GetRecordingsAsync(userId);
                }
                var recording = recordings.FirstOrDefault(r => r.Id == recordingId);
                
                if (recording == null) {
                    return Json(new { success = false, message = "录音文件不存在或无权限访问" });
                }

                var fileInfo = new FileInfo(recording.FilePath);
                var fileName = Path.GetFileName(recording.FilePath);
                var fileExtension = Path.GetExtension(recording.FilePath).ToUpperInvariant().TrimStart('.');
                
                string fileSizeText = "0 KB";
                if (fileInfo.Exists) {
                    var sizeInBytes = fileInfo.Length;
                    if (sizeInBytes < 1024)
                        fileSizeText = $"{sizeInBytes} B";
                    else if (sizeInBytes < 1024 * 1024)
                        fileSizeText = $"{sizeInBytes / 1024.0:F1} KB";
                    else if (sizeInBytes < 1024 * 1024 * 1024)
                        fileSizeText = $"{sizeInBytes / (1024.0 * 1024.0):F1} MB";
                    else
                        fileSizeText = $"{sizeInBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
                }

                var durationText = recording.Duration.ToString(@"mm\:ss");
                if (recording.Duration.TotalHours >= 1) {
                    durationText = recording.Duration.ToString(@"hh\:mm\:ss");
                }

                string quality = "未知";
                if (recording.Duration.TotalSeconds > 0 && fileInfo.Exists) {
                    var bitrate = (fileInfo.Length * 8) / recording.Duration.TotalSeconds / 1000; // kbps
                    if (bitrate < 64)
                        quality = "低质量 (~32kbps)";
                    else if (bitrate < 128)
                        quality = "标准质量 (~64kbps)";
                    else if (bitrate < 192)
                        quality = "高质量 (~128kbps)";
                    else if (bitrate < 320)
                        quality = "超高质量 (~192kbps)";
                    else
                        quality = "无损质量 (~320kbps+)";
                }

                string status = fileInfo.Exists ? "正常" : "文件丢失";
                
                return Json(new {
                    success = true,
                    data = new {
                        id = recording.Id,
                        fileName = fileName ?? $"recording_{recordingId}.{fileExtension.ToLower()}",
                        filePath = recording.FilePath,
                        sipUsername = recording.SipUsername ?? "未知联系人",
                        startTime = recording.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        duration = durationText,
                        durationSeconds = (int)recording.Duration.TotalSeconds,
                        fileSize = fileSizeText,
                        fileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                        quality = quality,
                        format = fileExtension,
                        status = status,
                        isFileExists = fileInfo.Exists,
                        createdDate = recording.StartTime.ToString("yyyy年MM月dd日"),
                        createdTime = recording.StartTime.ToString("HH:mm:ss"),
                        metadata = new {
                            canPlay = fileInfo.Exists && IsAudioFile(fileExtension),
                            canDownload = fileInfo.Exists,
                            lastModified = fileInfo.Exists ? fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") : null,
                            fullPath = recording.FilePath
                        }
                    }
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "获取录音详情时发生错误，录音ID: {RecordingId}", recordingId);
                return Json(new { success = false, message = "获取录音详情失败: " + ex.Message });
            }
        }

        /// <summary>
        /// 检查是否为音频文件
        /// </summary>
        private static bool IsAudioFile(string extension) {
            var audioExtensions = new[] { "MP3", "WAV", "OGG", "M4A", "AAC", "FLAC", "WMA" };
            return audioExtensions.Contains(extension.ToUpperInvariant());
        }

        [HttpGet]
        public async Task<IActionResult> Play(string filePath) {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var isAdmin = User.HasClaim("isAdmin", "True");

                if (string.IsNullOrEmpty(filePath)) {
                    return BadRequest("文件路径不能为空");
                }

                if (!System.IO.File.Exists(filePath)) {
                    _logger.LogWarning("尝试播放不存在的录音文件: {FilePath}", filePath);
                    return NotFound("录音文件不存在");
                }

                List<AI.Caller.Phone.Models.Recording> recordings;
                if (isAdmin) {
                    recordings = await _recordingService.GetAllRecordingsAsync();
                } else {
                    recordings = await _recordingService.GetRecordingsAsync(userId);
                }
                var recording = recordings.FirstOrDefault(r => r.FilePath == filePath);

                if (recording == null) {
                    _logger.LogWarning("用户 {UserId} 尝试访问无权限的录音文件: {FilePath}", userId, filePath);
                    return Forbid("无权限访问此录音文件");
                }

                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var contentType = "audio/mpeg";

                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                contentType = extension switch {
                    ".mp3" => "audio/mpeg",
                    ".wav" => "audio/wav",
                    ".ogg" => "audio/ogg",
                    ".m4a" => "audio/mp4",
                    _ => "audio/mpeg"
                };

                return File(fileStream, contentType, enableRangeProcessing: true);
            } catch (Exception ex) {
                _logger.LogError(ex, "播放录音文件时发生错误，文件路径: {FilePath}", filePath);
                return StatusCode(500, "播放录音文件时发生错误");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Download(string filePath) {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var isAdmin = User.HasClaim("isAdmin", "True");

                if (string.IsNullOrEmpty(filePath)) {
                    return BadRequest("文件路径不能为空");
                }

                if (!System.IO.File.Exists(filePath)) {
                    _logger.LogWarning("尝试下载不存在的录音文件: {FilePath}", filePath);
                    return NotFound("录音文件不存在");
                }

                List<AI.Caller.Phone.Models.Recording> recordings;
                if (isAdmin) {
                    recordings = await _recordingService.GetAllRecordingsAsync();
                } else {
                    recordings = await _recordingService.GetRecordingsAsync(userId);
                }
                var recording = recordings.FirstOrDefault(r => r.FilePath == filePath);

                if (recording == null) {
                    _logger.LogWarning("用户 {UserId} 尝试下载无权限的录音文件: {FilePath}", userId, filePath);
                    return Forbid("无权限下载此录音文件");
                }

                var fileName = Path.GetFileName(filePath);
                if (string.IsNullOrEmpty(fileName)) {
                    fileName = $"recording_{recording.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.mp3";
                }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                var contentType = "application/octet-stream";

                return File(fileBytes, contentType, fileName);
            } catch (Exception ex) {
                _logger.LogError(ex, "下载录音文件时发生错误，文件路径: {FilePath}", filePath);
                return StatusCode(500, "下载录音文件时发生错误");
            }
        }
    }
}