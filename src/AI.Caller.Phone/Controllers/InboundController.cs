using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers {
    [Authorize]
    public class InboundController : Controller {
        private readonly IInboundTemplateService _templateService;
        private readonly ILogger<InboundController> _logger;

        public InboundController(
            IInboundTemplateService templateService,
            ILogger<InboundController> logger) {
            _templateService = templateService;
            _logger = logger;
        }

        public async Task<IActionResult> Index() {
            try {
                var userId = GetCurrentUserId();
                var templates = await _templateService.GetUserTemplatesAsync(userId);
                return View(templates);
            } catch (Exception ex) {
                _logger.LogError(ex, "获取呼入模板列表失败");
                TempData["Error"] = "获取模板列表失败";
                return View(new List<InboundTemplate>());
            }
        }

        public async Task<IActionResult> Details(int id) {
            try {
                var template = await _templateService.GetTemplateAsync(id);
                if (template == null) {
                    TempData["Error"] = "模板不存在";
                    return RedirectToAction(nameof(Index));
                }

                return View(template);
            } catch (Exception ex) {
                _logger.LogError(ex, $"获取呼入模板详情失败: TemplateId={id}");
                TempData["Error"] = "获取模板详情失败";
                return RedirectToAction(nameof(Index));
            }
        }

        public IActionResult Create() {
            return View(new InboundTemplate());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(InboundTemplate template) {
            try {
                if (!ModelState.IsValid) {
                    return View(template);
                }

                var userId = GetCurrentUserId();
                template.UserId = userId;
                
                var createdTemplate = await _templateService.CreateTemplateAsync(template);
                TempData["Success"] = "模板创建成功";
                return RedirectToAction(nameof(Details), new { id = createdTemplate.Id });
            } catch (Exception ex) {
                _logger.LogError(ex, "创建呼入模板失败");
                TempData["Error"] = "创建模板失败";
                return View(template);
            }
        }

        public async Task<IActionResult> Edit(int id) {
            try {
                var template = await _templateService.GetTemplateAsync(id);
                if (template == null) {
                    TempData["Error"] = "模板不存在";
                    return RedirectToAction(nameof(Index));
                }

                return View(template);
            } catch (Exception ex) {
                _logger.LogError(ex, $"获取编辑模板失败: TemplateId={id}");
                TempData["Error"] = "获取模板失败";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, InboundTemplate template) {
            try {
                if (!ModelState.IsValid) {
                    return View(template);
                }

                var updatedTemplate = await _templateService.UpdateTemplateAsync(id, template);
                if (updatedTemplate == null) {
                    TempData["Error"] = "模板不存在";
                    return RedirectToAction(nameof(Index));
                }

                TempData["Success"] = "模板更新成功";
                return RedirectToAction(nameof(Details), new { id });
            } catch (Exception ex) {
                _logger.LogError(ex, $"更新呼入模板失败: TemplateId={id}");
                TempData["Error"] = "更新模板失败";
                return View(template);
            }
        }

        [HttpPost]
        public async Task<IActionResult> SetDefault(int id) {
            try {
                var userId = GetCurrentUserId();
                var success = await _templateService.SetDefaultTemplateAsync(id, userId);
                if (success) {
                    TempData["Success"] = "默认模板设置成功";
                } else {
                    TempData["Error"] = "设置默认模板失败";
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"设置默认呼入模板失败: TemplateId={id}");
                TempData["Error"] = "设置默认模板失败";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id) {
            try {
                var success = await _templateService.DeleteTemplateAsync(id);
                if (success) {
                    TempData["Success"] = "模板删除成功";
                } else {
                    TempData["Error"] = "模板不存在";
                }
            } catch (InvalidOperationException ex) {
                TempData["Error"] = ex.Message;
            } catch (Exception ex) {
                _logger.LogError(ex, $"删除呼入模板失败: TemplateId={id}");
                TempData["Error"] = "删除模板失败";
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