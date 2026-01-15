using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Services;
using AI.Caller.Core.Media.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Controllers;

[Authorize]
public class ScenarioRecordingController : Controller {
    private readonly IScenarioRecordingService _scenarioService;
    private readonly IDtmfInputService _dtmfService;
    private readonly AppDbContext _context;
    private readonly ILogger<ScenarioRecordingController> _logger;
    private readonly IAudioConverter _audioConverter;

    public ScenarioRecordingController(
        IScenarioRecordingService scenarioService,
        IDtmfInputService dtmfService,
        AppDbContext context,
        ILogger<ScenarioRecordingController> logger,
        IAudioConverter audioConverter) {
        _scenarioService = scenarioService;
        _dtmfService = dtmfService;
        _context = context;
        _logger = logger;
        _audioConverter = audioConverter;
    }

    // GET: ScenarioRecording
    public async Task<IActionResult> Index() {
        var scenarios = await _scenarioService.GetActiveScenarioRecordingsAsync();
        return View(scenarios);
    }

    // GET: ScenarioRecording/Details/5
    public async Task<IActionResult> Details(int id) {
        var scenario = await _scenarioService.GetScenarioRecordingAsync(id);
        if (scenario == null) {
            return NotFound();
        }
        return View(scenario);
    }

    // GET: ScenarioRecording/Create
    public IActionResult Create() {
        return View(new ScenarioRecording { Name = "", IsActive = true });
    }

    // POST: ScenarioRecording/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ScenarioRecording scenario) {
        if (ModelState.IsValid) {
            await _scenarioService.CreateScenarioRecordingAsync(scenario);
            return RedirectToAction(nameof(Index));
        }
        return View(scenario);
    }

    // GET: ScenarioRecording/Edit/5
    public async Task<IActionResult> Edit(int id) {
        var scenario = await _scenarioService.GetScenarioRecordingAsync(id);
        if (scenario == null) {
            return NotFound();
        }

        // 加载DTMF模板列表供选择
        ViewBag.DtmfTemplates = new SelectList(
            await _dtmfService.GetAllTemplatesAsync(),
            "Id",
            "Name");

        return View(scenario);
    }

    // POST: ScenarioRecording/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ScenarioRecording scenario) {
        if (id != scenario.Id) {
            return NotFound();
        }

        if (ModelState.IsValid) {
            try {
                await _scenarioService.UpdateScenarioRecordingAsync(scenario);
                return RedirectToAction(nameof(Index));
            } catch (DbUpdateConcurrencyException) {
                if (!await ScenarioExists(id)) {
                    return NotFound();
                }
                throw;
            }
        }

        ViewBag.DtmfTemplates = new SelectList(
            await _dtmfService.GetAllTemplatesAsync(),
            "Id",
            "Name");

        return View(scenario);
    }

    // GET: ScenarioRecording/Delete/5
    public async Task<IActionResult> Delete(int id) {
        var scenario = await _scenarioService.GetScenarioRecordingAsync(id);
        if (scenario == null) {
            return NotFound();
        }
        return View(scenario);
    }

    // POST: ScenarioRecording/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id) {
        await _scenarioService.DeleteScenarioRecordingAsync(id);
        return RedirectToAction(nameof(Index));
    }

    // API: 添加片段
    [HttpPost]
    public async Task<IActionResult> AddSegment([FromBody] ScenarioRecordingSegment segment) {
        try {
            await _scenarioService.AddSegmentAsync(segment);
            return Json(new { success = true, segmentId = segment.Id });
        } catch (Exception ex) {
            _logger.LogError(ex, "添加片段失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // API: 更新片段
    [HttpPost]
    public async Task<IActionResult> UpdateSegment([FromBody] ScenarioRecordingSegment segment) {
        try {
            await _scenarioService.UpdateSegmentAsync(segment);
            return Json(new { success = true });
        } catch (Exception ex) {
            _logger.LogError(ex, "更新片段失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // API: 删除片段
    [HttpPost]
    public async Task<IActionResult> DeleteSegment(int segmentId) {
        try {
            await _scenarioService.DeleteSegmentAsync(segmentId);
            return Json(new { success = true });
        } catch (Exception ex) {
            _logger.LogError(ex, "删除片段失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // API: 重新排序片段
    [HttpPost]
    public async Task<IActionResult> ReorderSegments(int scenarioId, [FromBody] List<int> segmentIds) {
        try {
            await _scenarioService.ReorderSegmentsAsync(scenarioId, segmentIds);
            return Json(new { success = true });
        } catch (Exception ex) {
            _logger.LogError(ex, "重新排序失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // API: 获取片段列表
    [HttpGet]
    public async Task<IActionResult> GetSegments(int scenarioId) {
        try {
            var segments = await _scenarioService.GetSegmentsAsync(scenarioId);
            return Json(new { success = true, segments });
        } catch (Exception ex) {
            _logger.LogError(ex, "获取片段列表失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // API: 获取单个片段
    [HttpGet]
    public async Task<IActionResult> GetSegment(int segmentId) {
        try {
            var segment = await _context.ScenarioRecordingSegments
                .FirstOrDefaultAsync(s => s.Id == segmentId);

            if (segment == null) {
                return Json(new { success = false, message = "片段不存在" });
            }

            return Json(new { success = true, segment });
        } catch (Exception ex) {
            _logger.LogError(ex, "获取片段失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    private async Task<bool> ScenarioExists(int id) {
        return await _context.ScenarioRecordings.AnyAsync(e => e.Id == id);
    }

    // ==================== 在线录音功能 ====================

    /// <summary>
    /// 上传在线录音文件
    /// </summary>
    /// <param name="file">音频文件</param>
    /// <param name="fileName">文件名</param>
    /// <returns>上传结果，包含文件路径</returns>
    [HttpPost]
    public async Task<IActionResult> UploadOnlineRecording(IFormFile file, [FromForm] string? fileName = null) {
        try {
            // 1. 验证文件
            if (file == null || file.Length == 0) {
                return BadRequest(new { success = false, message = "文件不能为空" });
            }

            // 验证文件大小（10MB）
            if (file.Length > 10 * 1024 * 1024) {
                return BadRequest(new { success = false, message = "文件大小不能超过10MB" });
            }

            // 验证文件类型
            var allowedExtensions = new[] { ".webm", ".wav", ".mp3", ".ogg", ".m4a", ".pcm" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension)) {
                return BadRequest(new { success = false, message = "不支持的音频格式" });
            }

            // 2. 生成唯一文件名
            var uniqueFileName = $"{Guid.NewGuid()}{extension}";

            // 3. 保存原始文件到临时目录
            var tempDir = Path.Combine("recordings", "online", "temp");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, uniqueFileName);

            using (var stream = new FileStream(tempPath, FileMode.Create)) {
                await file.CopyToAsync(stream);
            }

            _logger.LogInformation("在线录音文件已保存到临时目录: {TempPath}", tempPath);

            bool isPcmFile = extension.Equals(".pcm", StringComparison.OrdinalIgnoreCase);
            string finalPath;

            if (isPcmFile) {
                // 4a. 处理 PCM 文件
                var finalDir = Path.Combine("recordings", "online", "final");
                Directory.CreateDirectory(finalDir);
                var pcmFileName = Path.ChangeExtension(uniqueFileName, ".pcm");
                finalPath = Path.Combine(finalDir, pcmFileName);
                System.IO.File.Move(tempPath, finalPath);                
            } else {
                // 4. 转换为PCM格式（如果需要）
                var finalDir = Path.Combine("recordings", "online", "final");
                Directory.CreateDirectory(finalDir);
                var pcmFileName = Path.ChangeExtension(uniqueFileName, ".pcm");
                finalPath = Path.Combine(finalDir, pcmFileName);

                try {
                    await ConvertToPcmAsync(tempPath, finalPath);
                    _logger.LogInformation("音频文件已转换为PCM格式: {FinalPath}", finalPath);
                } catch (Exception ex) {
                    _logger.LogError(ex, "音频格式转换失败");
                    throw;
                }
            }

            // 5. 返回路径
            return Ok(new {
                success = true,
                filePath = finalPath,
                fileName = Path.GetFileName(finalPath),
                message = "录音上传成功"
            });

        } catch (Exception ex) {
            _logger.LogError(ex, "上传在线录音失败");
            return StatusCode(500, new { success = false, message = "上传失败: " + ex.Message });
        }
    }

    /// <summary>
    /// 使用FFmpeg.AutoGen将音频转换为PCM格式
    /// </summary>
    /// <param name="inputPath">输入文件路径</param>
    /// <param name="outputPath">输出文件路径</param>
    private async Task ConvertToPcmAsync(string inputPath, string outputPath) {
        // 使用依赖注入的 AudioConverter 服务
        // 转换为 PCM 格式：8000Hz 采样率，单声道
        bool success = await _audioConverter.ConvertToPcmAsync(inputPath, outputPath, sampleRate: 8000, channels: 1);

        if (!success) {
            throw new Exception("音频格式转换失败");
        }

        _logger.LogInformation("音频转换成功: {InputPath} -> {OutputPath}", inputPath, outputPath);
    }
}
