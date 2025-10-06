using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Mvc;
using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AI.Caller.Phone.Controllers {
    public class AICustomerServiceController : Controller {
        private readonly IAICustomerServiceSettingsProvider _settingsProvider;
        private readonly AppDbContext _context;

        public AICustomerServiceController(IAICustomerServiceSettingsProvider settingsProvider, AppDbContext context) {
            _settingsProvider = settingsProvider;
            _context = context;
        }

        public async Task<IActionResult> Index() {
            var settings = await _settingsProvider.GetSettingsAsync();
            ViewBag.TtsTemplates = new SelectList(await _context.TtsTemplates.Where(t => t.IsActive).ToListAsync(), "Id", "Name", settings.DefaultTtsTemplateId);
            return View(settings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(AICustomerServiceSettings settings) {
            if (ModelState.IsValid) {
                await _settingsProvider.UpdateSettingsAsync(settings);
                return RedirectToAction(nameof(Index));
            }
            ViewBag.TtsTemplates = new SelectList(await _context.TtsTemplates.Where(t => t.IsActive).ToListAsync(), "Id", "Name", settings.DefaultTtsTemplateId);
            return View(settings);
        }
    }
}