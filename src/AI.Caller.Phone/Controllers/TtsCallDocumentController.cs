using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers {
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TtsCallDocumentController : ControllerBase {
        private readonly ITtsCallDocumentService _documentService;
        private readonly ILogger<TtsCallDocumentController> _logger;

        public TtsCallDocumentController(
            ITtsCallDocumentService documentService,
            ILogger<TtsCallDocumentController> logger) {
            _documentService = documentService;
            _logger = logger;
        }

        [HttpGet("template")]
        public async Task<IActionResult> DownloadTemplate() {
            try {
                var templateBytes = await _documentService.GenerateTemplateAsync();
                return File(templateBytes, 
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "TTS外呼模板.xlsx");
            } catch (Exception ex) {
                _logger.LogError(ex, "下载TTS外呼模板失败");
                return StatusCode(500, "下载模板失败");
            }
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocument(IFormFile file) {
            try {
                var userId = GetCurrentUserId();
                var document = await _documentService.UploadDocumentAsync(file, userId);
                
                return Ok(new {
                    success = true,
                    message = "文档上传成功",
                    data = new {
                        id = document.Id,
                        fileName = document.FileName,
                        totalRecords = document.TotalRecords,
                        uploadTime = document.UploadTime
                    }
                });
            } catch (ArgumentException ex) {
                return BadRequest(new { success = false, message = ex.Message });
            } catch (Exception ex) {
                _logger.LogError(ex, "上传TTS外呼文档失败");
                return StatusCode(500, new { success = false, message = "上传失败" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDocuments() {
            try {
                var userId = GetCurrentUserId();
                var documents = await _documentService.GetUserDocumentsAsync(userId);
                
                return Ok(new {
                    success = true,
                    data = documents.Select(d => new {
                        id = d.Id,
                        fileName = d.FileName,
                        totalRecords = d.TotalRecords,
                        completedCalls = d.CompletedCalls,
                        failedCalls = d.FailedCalls,
                        status = d.Status.ToString(),
                        uploadTime = d.UploadTime,
                        startTime = d.StartTime,
                        endTime = d.EndTime
                    })
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "获取TTS外呼文档列表失败");
                return StatusCode(500, new { success = false, message = "获取列表失败" });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetDocument(int id) {
            try {
                var document = await _documentService.GetDocumentAsync(id);
                if (document == null) {
                    return NotFound(new { success = false, message = "文档不存在" });
                }

                return Ok(new {
                    success = true,
                    data = new {
                        id = document.Id,
                        fileName = document.FileName,
                        totalRecords = document.TotalRecords,
                        completedCalls = document.CompletedCalls,
                        failedCalls = document.FailedCalls,
                        status = document.Status.ToString(),
                        uploadTime = document.UploadTime,
                        startTime = document.StartTime,
                        endTime = document.EndTime,
                        callRecords = document.CallRecords.Select(r => new {
                            id = r.Id,
                            phoneNumber = r.PhoneNumber,
                            gender = r.Gender,
                            addressTemplate = r.AddressTemplate,
                            ttsContent = r.TtsContent,
                            callStatus = r.CallStatus.ToString(),
                            callTime = r.CallTime,
                            failureReason = r.FailureReason,
                            retryCount = r.RetryCount
                        })
                    }
                });
            } catch (Exception ex) {
                _logger.LogError(ex, $"获取TTS外呼文档详情失败: DocumentId={id}");
                return StatusCode(500, new { success = false, message = "获取详情失败" });
            }
        }

        [HttpPost("{id}/start")]
        public async Task<IActionResult> StartCallTask(int id) {
            try {
                var success = await _documentService.StartCallTaskAsync(id);
                if (!success) {
                    return BadRequest(new { success = false, message = "启动任务失败，请检查文档状态" });
                }

                return Ok(new { success = true, message = "任务启动成功" });
            } catch (Exception ex) {
                _logger.LogError(ex, $"启动TTS外呼任务失败: DocumentId={id}");
                return StatusCode(500, new { success = false, message = "启动任务失败" });
            }
        }

        [HttpPost("{id}/pause")]
        public async Task<IActionResult> PauseCallTask(int id) {
            try {
                var success = await _documentService.PauseCallTaskAsync(id);
                if (!success) {
                    return BadRequest(new { success = false, message = "暂停任务失败，请检查任务状态" });
                }

                return Ok(new { success = true, message = "任务暂停成功" });
            } catch (Exception ex) {
                _logger.LogError(ex, $"暂停TTS外呼任务失败: DocumentId={id}");
                return StatusCode(500, new { success = false, message = "暂停任务失败" });
            }
        }

        [HttpPost("{id}/resume")]
        public async Task<IActionResult> ResumeCallTask(int id) {
            try {
                var success = await _documentService.ResumeCallTaskAsync(id);
                if (!success) {
                    return BadRequest(new { success = false, message = "恢复任务失败，请检查任务状态" });
                }

                return Ok(new { success = true, message = "任务恢复成功" });
            } catch (Exception ex) {
                _logger.LogError(ex, $"恢复TTS外呼任务失败: DocumentId={id}");
                return StatusCode(500, new { success = false, message = "恢复任务失败" });
            }
        }

        [HttpPost("{id}/stop")]
        public async Task<IActionResult> StopCallTask(int id) {
            try {
                var success = await _documentService.StopCallTaskAsync(id);
                if (!success) {
                    return BadRequest(new { success = false, message = "停止任务失败，请检查任务状态" });
                }

                return Ok(new { success = true, message = "任务停止成功" });
            } catch (Exception ex) {
                _logger.LogError(ex, $"停止TTS外呼任务失败: DocumentId={id}");
                return StatusCode(500, new { success = false, message = "停止任务失败" });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDocument(int id) {
            try {
                var success = await _documentService.DeleteDocumentAsync(id);
                if (!success) {
                    return NotFound(new { success = false, message = "文档不存在" });
                }

                return Ok(new { success = true, message = "文档删除成功" });
            } catch (InvalidOperationException ex) {
                return BadRequest(new { success = false, message = ex.Message });
            } catch (Exception ex) {
                _logger.LogError(ex, $"删除TTS外呼文档失败: DocumentId={id}");
                return StatusCode(500, new { success = false, message = "删除失败" });
            }
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