using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers {
    [Authorize]
    public class TtsCallController : Controller {
        private readonly ITtsCallDocumentService _documentService;
        private readonly ILogger<TtsCallController> _logger;

        public TtsCallController(
            ITtsCallDocumentService documentService,
            ILogger<TtsCallController> logger) {
            _documentService = documentService;
            _logger = logger;
        }

        public async Task<IActionResult> Index() {
            try {
                var userId = GetCurrentUserId();
                var documents = await _documentService.GetUserDocumentsAsync(userId);
                return View(documents);
            } catch (Exception ex) {
                _logger.LogError(ex, "获取TTS外呼文档列表失败");
                TempData["Error"] = "获取文档列表失败";
                return View(new List<TtsCallDocument>());
            }
        }

        public async Task<IActionResult> Details(int id) {
            try {
                var document = await _documentService.GetDocumentAsync(id);
                if (document == null) {
                    TempData["Error"] = "文档不存在";
                    return RedirectToAction(nameof(Index));
                }

                return View(document);
            } catch (Exception ex) {
                _logger.LogError(ex, $"获取TTS外呼文档详情失败: DocumentId={id}");
                TempData["Error"] = "获取文档详情失败";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadTemplate() {
            try {
                var templateBytes = await _documentService.GenerateTemplateAsync();
                return File(templateBytes, 
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "TTS外呼模板.xlsx");
            } catch (Exception ex) {
                _logger.LogError(ex, "下载TTS外呼模板失败");
                TempData["Error"] = "下载模板失败";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file) {
            try {
                if (file == null || file.Length == 0) {
                    TempData["Error"] = "请选择要上传的文件";
                    return RedirectToAction(nameof(Index));
                }

                var userId = GetCurrentUserId();
                var document = await _documentService.UploadDocumentAsync(file, userId);
                
                TempData["Success"] = $"文档上传成功，共解析 {document.TotalRecords} 条记录";
                return RedirectToAction(nameof(Details), new { id = document.Id });
            } catch (ArgumentException ex) {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            } catch (Exception ex) {
                _logger.LogError(ex, "上传TTS外呼文档失败");
                TempData["Error"] = "上传文档失败";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        public async Task<IActionResult> StartTask(int id) {
            try {
                var success = await _documentService.StartCallTaskAsync(id);
                if (success) {
                    TempData["Success"] = "任务启动成功";
                } else {
                    TempData["Error"] = "启动任务失败，请检查文档状态";
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"启动TTS外呼任务失败: DocumentId={id}");
                TempData["Error"] = "启动任务失败";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        public async Task<IActionResult> PauseTask(int id) {
            try {
                var success = await _documentService.PauseCallTaskAsync(id);
                if (success) {
                    TempData["Success"] = "任务暂停成功";
                } else {
                    TempData["Error"] = "暂停任务失败，请检查任务状态";
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"暂停TTS外呼任务失败: DocumentId={id}");
                TempData["Error"] = "暂停任务失败";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        public async Task<IActionResult> ResumeTask(int id) {
            try {
                var success = await _documentService.ResumeCallTaskAsync(id);
                if (success) {
                    TempData["Success"] = "任务恢复成功";
                } else {
                    TempData["Error"] = "恢复任务失败，请检查任务状态";
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"恢复TTS外呼任务失败: DocumentId={id}");
                TempData["Error"] = "恢复任务失败";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        public async Task<IActionResult> StopTask(int id) {
            try {
                var success = await _documentService.StopCallTaskAsync(id);
                if (success) {
                    TempData["Success"] = "任务停止成功";
                } else {
                    TempData["Error"] = "停止任务失败";
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"停止TTS外呼任务失败: DocumentId={id}");
                TempData["Error"] = "停止任务失败";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id) {
            try {
                var success = await _documentService.DeleteDocumentAsync(id);
                if (success) {
                    TempData["Success"] = "文档删除成功";
                } else {
                    TempData["Error"] = "文档不存在";
                }
            } catch (InvalidOperationException ex) {
                TempData["Error"] = ex.Message;
            } catch (Exception ex) {
                _logger.LogError(ex, $"删除TTS外呼文档失败: DocumentId={id}");
                TempData["Error"] = "删除文档失败";
            }

            return RedirectToAction(nameof(Index));
        }

        private int GetCurrentUserId() {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId)) {
                throw new UnauthorizedAccessException("无法获取用户信息");
            }
            return userId;
        }
    }
}