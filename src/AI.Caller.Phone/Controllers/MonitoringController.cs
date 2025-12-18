using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers;

[Authorize]
public class MonitoringController : Controller {
    private readonly ILogger _logger;
    private readonly AppDbContext _context;
    private readonly IMonitoringService _monitoringService;
    private readonly AICustomerServiceManager _aiServiceManager;
    private readonly IPlaybackControlService _playbackControlService;

    public MonitoringController(
        AppDbContext context,
        ILogger<MonitoringController> logger,
        IMonitoringService monitoringService,
        AICustomerServiceManager aiServiceManager,
        IPlaybackControlService playbackControlService
     ) {
        _logger = logger;
        _context = context;
        _aiServiceManager = aiServiceManager;
        _monitoringService = monitoringService;
        _playbackControlService = playbackControlService;
    }

    // GET: Monitoring
    public async Task<IActionResult> Index() {
        var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
        var sessions = await _monitoringService.GetUserSessionsAsync(userId);
        return View(sessions);
    }

    public async Task<IActionResult> MonitorStop(int id) {
        await _monitoringService.StopMonitoringAsync(id);
        return RedirectToAction("Index");
    }

    // GET: Monitoring/ActiveCalls
    public IActionResult ActiveCalls() {
        return View();
    }

    // API: 获取活跃通话列表
    [HttpGet]
    public IActionResult GetActiveCalls() {
        try {
            var activeSessions = _aiServiceManager.GetAllActiveSessions();
            var sessionList = activeSessions.Select(session => new {
                userId = session.User.Id,
                callId = $"Call-{session.User.Id}-{session.StartTime:yyyyMMddHHmmss}",
                userName = session.User.Username,
                startTime = session.StartTime,
                duration = (DateTime.Now - session.StartTime).TotalSeconds,
                isMonitored = _aiServiceManager.IsCallBeingMonitored(session.User.Id),
                monitorCount = _aiServiceManager.GetCallMonitors(session.User.Id).Count
            }).ToList();

            return Json(new { success = true, sessions = sessionList });
        } catch (Exception ex) {
            _logger.LogError(ex, "获取活跃通话列表失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // GET: Monitoring/Monitor/5
    public async Task<IActionResult> Monitor(int userId, string callId) {
        ViewBag.TargetUserId = userId;
        ViewBag.CallId = callId;

        // 获取用户信息
        var user = await _context.Users.FindAsync(userId);
        ViewBag.TargetUserName = user?.Username ?? "Unknown";

        // 获取场景片段列表（服务端准备数据）
        var session = _aiServiceManager.GetSessionByCallId(callId);
        if (session?.ScenarioRecording != null) {
            var segments = session.ScenarioRecording.Segments
                .OrderBy(s => s.SegmentOrder)
                .ToList();
            ViewBag.ScenarioSegments = segments;
            ViewBag.ScenarioName = session.ScenarioRecording.Name;
        } else {
            ViewBag.ScenarioSegments = new List<ScenarioRecordingSegment>();
            ViewBag.ScenarioName = null;
        }

        return View();
    }

    // API: 获取监听会话历史
    [HttpGet]
    public async Task<IActionResult> GetMonitoringHistory(int userId) {
        try {
            var sessions = await _monitoringService.GetUserSessionsAsync(userId);
            return Json(new { success = true, sessions });
        } catch (Exception ex) {
            _logger.LogError(ex, "获取监听历史失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // API: 获取通话监听者列表
    [HttpGet]
    public IActionResult GetCallMonitors(int userId) {
        try {
            var monitors = _aiServiceManager.GetCallMonitors(userId);
            return Json(new { success = true, monitors });
        } catch (Exception ex) {
            _logger.LogError(ex, "获取监听者列表失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // API: 检查通话是否被监听
    [HttpGet]
    public IActionResult IsCallBeingMonitored(int userId) {
        try {
            var isMonitored = _aiServiceManager.IsCallBeingMonitored(userId);
            return Json(new { success = true, isMonitored });
        } catch (Exception ex) {
            _logger.LogError(ex, "检查监听状态失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // API: 获取播放控制状态
    [HttpGet]
    public async Task<IActionResult> GetPlaybackState(string callId) {
        try {
            var state = await _playbackControlService.GetPlaybackControlAsync(callId);
            return Json(new { success = true, state });
        } catch (Exception ex) {
            _logger.LogError(ex, "获取播放状态失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // API: 获取场景片段列表
    [HttpGet]
    public IActionResult GetScenarioSegments(string callId) {
        try {
            // 根据callId从活跃会话中获取场景信息
            var session = _aiServiceManager.GetSessionByCallId(callId);
            
            if (session == null) {
                _logger.LogWarning("未找到CallId对应的会话: {CallId}", callId);
                return Json(new { success = false, message = "未找到对应的通话会话" });
            }

            if (session.ScenarioRecording == null) {
                _logger.LogWarning("会话中没有场景录音信息: {CallId}", callId);
                return Json(new { success = false, message = "该通话未使用场景录音模式" });
            }

            var scenario = session.ScenarioRecording;
            
            // 返回场景片段列表
            var segments = scenario.Segments
                .OrderBy(s => s.SegmentOrder)
                .Select(s => new {
                    id = s.Id,
                    segmentOrder = s.SegmentOrder,
                    segmentType = (int)s.SegmentType,
                    filePath = s.FilePath,
                    ttsText = s.TtsText,
                    conditionExpression = s.ConditionExpression,
                    duration = s.Duration
                }).ToList();

            _logger.LogInformation("获取场景片段成功: CallId={CallId}, ScenarioId={ScenarioId}, SegmentCount={Count}",
                callId, scenario.Id, segments.Count);

            return Json(new { 
                success = true, 
                scenarioId = scenario.Id,
                scenarioName = scenario.Name,
                segments 
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "获取场景片段失败: CallId={CallId}", callId);
            return Json(new { success = false, message = $"获取场景片段失败: {ex.Message}" });
        }
    }
}
