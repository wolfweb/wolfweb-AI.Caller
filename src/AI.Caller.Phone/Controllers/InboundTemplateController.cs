using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers {
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class InboundTemplateController : ControllerBase {
        private readonly IInboundTemplateService _templateService;
        private readonly ILogger<InboundTemplateController> _logger;

        public InboundTemplateController(
            IInboundTemplateService templateService,
            ILogger<InboundTemplateController> logger) {
            _templateService = templateService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetTemplates() {
            try {
                var userId = GetCurrentUserId();
                var templates = await _templateService.GetUserTemplatesAsync(userId);
                
                return Ok(new {
                    success = true,
                    data = templates.Select(t => new {
                        id = t.Id,
                        name = t.Name,
                        description = t.Description,
                        isDefault = t.IsDefault,
                        isActive = t.IsActive,
                        createdTime = t.CreatedTime,
                        updatedTime = t.UpdatedTime
                    })
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "获取呼入模板列表失败");
                return StatusCode(500, new { success = false, message = "获取列表失败" });
            }
        }

        /// <summary>
        /// 获取呼入模板详情
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTemplate(int id) {
            try {
                var template = await _templateService.GetTemplateAsync(id);
                if (template == null) {
                    return NotFound(new { success = false, message = "模板不存在" });
                }

                return Ok(new {
                    success = true,
                    data = new {
                        id = template.Id,
                        name = template.Name,
                        description = template.Description,
                        welcomeScript = template.WelcomeScript,
                        responseRules = template.ResponseRules,
                        isDefault = template.IsDefault,
                        isActive = template.IsActive,
                        createdTime = template.CreatedTime,
                        updatedTime = template.UpdatedTime
                    }
                });
            } catch (Exception ex) {
                _logger.LogError(ex, $"获取呼入模板详情失败: TemplateId={id}");
                return StatusCode(500, new { success = false, message = "获取详情失败" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateTemplate([FromBody] CreateInboundTemplateRequest request) {
            try {
                var userId = GetCurrentUserId();
                var template = new InboundTemplate {
                    Name = request.Name,
                    Description = request.Description ?? "",
                    WelcomeScript = request.WelcomeScript,
                    ResponseRules = request.ResponseRules,
                    IsActive = request.IsActive,
                    UserId = userId
                };

                var createdTemplate = await _templateService.CreateTemplateAsync(template);
                
                return Ok(new {
                    success = true,
                    message = "模板创建成功",
                    data = new {
                        id = createdTemplate.Id,
                        name = createdTemplate.Name,
                        createdTime = createdTemplate.CreatedTime
                    }
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "创建呼入模板失败");
                return StatusCode(500, new { success = false, message = "创建失败" });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTemplate(int id, [FromBody] UpdateInboundTemplateRequest request) {
            try {
                var template = new InboundTemplate {
                    Name = request.Name,
                    Description = request.Description ?? "",
                    WelcomeScript = request.WelcomeScript,
                    ResponseRules = request.ResponseRules,
                    IsActive = request.IsActive
                };

                var updatedTemplate = await _templateService.UpdateTemplateAsync(id, template);
                if (updatedTemplate == null) {
                    return NotFound(new { success = false, message = "模板不存在" });
                }

                return Ok(new {
                    success = true,
                    message = "模板更新成功",
                    data = new {
                        id = updatedTemplate.Id,
                        name = updatedTemplate.Name,
                        updatedTime = updatedTemplate.UpdatedTime
                    }
                });
            } catch (Exception ex) {
                _logger.LogError(ex, $"更新呼入模板失败: TemplateId={id}");
                return StatusCode(500, new { success = false, message = "更新失败" });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTemplate(int id) {
            try {
                var success = await _templateService.DeleteTemplateAsync(id);
                if (!success) {
                    return NotFound(new { success = false, message = "模板不存在" });
                }

                return Ok(new { success = true, message = "模板删除成功" });
            } catch (InvalidOperationException ex) {
                return BadRequest(new { success = false, message = ex.Message });
            } catch (Exception ex) {
                _logger.LogError(ex, $"删除呼入模板失败: TemplateId={id}");
                return StatusCode(500, new { success = false, message = "删除失败" });
            }
        }

        [HttpPost("{id}/set-default")]
        public async Task<IActionResult> SetDefaultTemplate(int id) {
            try {
                var userId = GetCurrentUserId();
                var success = await _templateService.SetDefaultTemplateAsync(id, userId);
                if (!success) {
                    return BadRequest(new { success = false, message = "设置默认模板失败" });
                }

                return Ok(new { success = true, message = "默认模板设置成功" });
            } catch (Exception ex) {
                _logger.LogError(ex, $"设置默认呼入模板失败: TemplateId={id}");
                return StatusCode(500, new { success = false, message = "设置失败" });
            }
        }

        [HttpGet("default")]
        public async Task<IActionResult> GetDefaultTemplate() {
            try {
                var userId = GetCurrentUserId();
                var template = await _templateService.GetDefaultTemplateAsync(userId);
                
                if (template == null) {
                    return Ok(new { success = true, data = (object?)null, message = "未设置默认模板" });
                }

                return Ok(new {
                    success = true,
                    data = new {
                        id = template.Id,
                        name = template.Name,
                        description = template.Description,
                        welcomeScript = template.WelcomeScript,
                        responseRules = template.ResponseRules
                    }
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "获取默认呼入模板失败");
                return StatusCode(500, new { success = false, message = "获取失败" });
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

    public class CreateInboundTemplateRequest {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string WelcomeScript { get; set; } = string.Empty;
        public string? ResponseRules { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class UpdateInboundTemplateRequest {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string WelcomeScript { get; set; } = string.Empty;
        public string? ResponseRules { get; set; }
        public bool IsActive { get; set; } = true;
    }
}