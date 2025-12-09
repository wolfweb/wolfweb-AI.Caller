using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Controllers;

[Authorize]
public class DtmfTemplateController : Controller {
    private readonly IDtmfInputService _dtmfService;
    private readonly AppDbContext _context;
    private readonly ILogger<DtmfTemplateController> _logger;

    public DtmfTemplateController(
        IDtmfInputService dtmfService,
        AppDbContext context,
        ILogger<DtmfTemplateController> logger) {
        _dtmfService = dtmfService;
        _context = context;
        _logger = logger;
    }

    // GET: DtmfTemplate
    public async Task<IActionResult> Index() {
        var templates = await _dtmfService.GetAllTemplatesAsync();
        return View(templates);
    }

    // GET: DtmfTemplate/Details/5
    public async Task<IActionResult> Details(int id) {
        var template = await _dtmfService.GetTemplateAsync(id);
        if (template == null) {
            return NotFound();
        }
        return View(template);
    }

    // GET: DtmfTemplate/Create
    public IActionResult Create() {
        return View(new DtmfInputTemplate {
            MaxLength = 18,
            MinLength = 1,
            TerminationKey = '#',
            BackspaceKey = '*',
            TimeoutSeconds = 30,
            MaxRetries = 3,
            ValidatorType = "Numeric",
            InputType = DtmfInputType.Numeric,
            Name = ""
        });
    }

    // POST: DtmfTemplate/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DtmfInputTemplate template) {
        if (ModelState.IsValid) {
            await _dtmfService.CreateTemplateAsync(template);
            return RedirectToAction(nameof(Index));
        }
        return View(template);
    }

    // GET: DtmfTemplate/Edit/5
    public async Task<IActionResult> Edit(int id) {
        var template = await _dtmfService.GetTemplateAsync(id);
        if (template == null) {
            return NotFound();
        }
        return View(template);
    }

    // POST: DtmfTemplate/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, DtmfInputTemplate template) {
        if (id != template.Id) {
            return NotFound();
        }

        if (ModelState.IsValid) {
            try {
                await _dtmfService.UpdateTemplateAsync(template);
                return RedirectToAction(nameof(Index));
            } catch (DbUpdateConcurrencyException) {
                if (!await TemplateExists(id)) {
                    return NotFound();
                }
                throw;
            }
        }
        return View(template);
    }

    // GET: DtmfTemplate/Delete/5
    public async Task<IActionResult> Delete(int id) {
        var template = await _dtmfService.GetTemplateAsync(id);
        if (template == null) {
            return NotFound();
        }
        return View(template);
    }

    // POST: DtmfTemplate/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id) {
        await _dtmfService.DeleteTemplateAsync(id);
        return RedirectToAction(nameof(Index));
    }

    // API: 测试验证
    [HttpPost]
    public async Task<IActionResult> TestValidation([FromBody] TestValidationRequest request) {
        try {
            var result = await _dtmfService.ValidateInputAsync(request.TemplateId, request.Input);
            return Json(new {
                success = true,
                isValid = result.IsValid,
                errorMessage = result.ErrorMessage
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "测试验证失败");
            return Json(new { success = false, message = ex.Message });
        }
    }

    private async Task<bool> TemplateExists(int id) {
        return await _context.DtmfInputTemplates.AnyAsync(e => e.Id == id);
    }
}

public record TestValidationRequest(int TemplateId, string Input);
