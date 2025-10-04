using AI.Caller.Phone.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace AI.Caller.Phone.Controllers {
    [Authorize]
    public class TtsTemplatesController : Controller {
        private readonly AppDbContext _context;

        public TtsTemplatesController(AppDbContext context) {
            _context = context;
        }

        public async Task<IActionResult> Index() {
            var templates = await _context.TtsTemplates.Include(t => t.Variables).ToListAsync();
            return View(templates);
        }

        public async Task<IActionResult> Details(int? id) {
            if (id == null) {
                return NotFound();
            }

            var ttsTemplate = await _context.TtsTemplates
                .Include(t => t.Variables)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (ttsTemplate == null) {
                return NotFound();
            }

            return View(ttsTemplate);
        }

        public IActionResult Create() {
            ViewData["AllVariables"] = new MultiSelectList(_context.TtsVariables, "Id", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Content,IsActive,PlayCount,HangupAfterPlay")] TtsTemplate ttsTemplate, int[] selectedVariables) {
            if (ModelState.IsValid) {
                if (selectedVariables != null) {
                    foreach (var variableId in selectedVariables) {
                        var variableToAdd = await _context.TtsVariables.FindAsync(variableId);
                        if (variableToAdd != null) {
                            ttsTemplate.Variables.Add(variableToAdd);
                        }
                    }
                }
                _context.Add(ttsTemplate);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["AllVariables"] = new MultiSelectList(_context.TtsVariables, "Id", "Name", selectedVariables);
            return View(ttsTemplate);
        }

        public async Task<IActionResult> Edit(int? id) {
            if (id == null) {
                return NotFound();
            }

            var ttsTemplate = await _context.TtsTemplates
                .Include(t => t.Variables)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (ttsTemplate == null) {
                return NotFound();
            }

            var selectedVariables = ttsTemplate.Variables.Select(v => v.Id).ToList();
            ViewData["AllVariables"] = new MultiSelectList(_context.TtsVariables, "Id", "Name", selectedVariables);

            return View(ttsTemplate);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Content,IsActive,PlayCount,HangupAfterPlay")] TtsTemplate ttsTemplate, int[] selectedVariables) {
            if (id != ttsTemplate.Id) {
                return NotFound();
            }

            var templateToUpdate = await _context.TtsTemplates
                .Include(t => t.Variables)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (templateToUpdate == null) {
                return NotFound();
            }

            if (ModelState.IsValid) {
                try {
                    templateToUpdate.Name = ttsTemplate.Name;
                    templateToUpdate.Content = ttsTemplate.Content;
                    templateToUpdate.IsActive = ttsTemplate.IsActive;
                    templateToUpdate.PlayCount = ttsTemplate.PlayCount;
                    templateToUpdate.HangupAfterPlay = ttsTemplate.HangupAfterPlay;

                    templateToUpdate.Variables.Clear();
                    if (selectedVariables != null) {
                        foreach (var variableId in selectedVariables) {
                            var variableToAdd = await _context.TtsVariables.FindAsync(variableId);
                            if (variableToAdd != null) {
                                templateToUpdate.Variables.Add(variableToAdd);
                            }
                        }
                    }

                    await _context.SaveChangesAsync();
                } catch (DbUpdateConcurrencyException) {
                    if (!TtsTemplateExists(ttsTemplate.Id)) {
                        return NotFound();
                    } else {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }

            ViewData["AllVariables"] = new MultiSelectList(_context.TtsVariables, "Id", "Name", selectedVariables);
            return View(templateToUpdate);
        }

        public async Task<IActionResult> Delete(int? id) {
            if (id == null) {
                return NotFound();
            }

            var ttsTemplate = await _context.TtsTemplates
                .Include(t => t.Variables)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (ttsTemplate == null) {
                return NotFound();
            }

            return View(ttsTemplate);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id) {
            var ttsTemplate = await _context.TtsTemplates.FindAsync(id);
            _context.TtsTemplates.Remove(ttsTemplate);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool TtsTemplateExists(int id) {
            return _context.TtsTemplates.Any(e => e.Id == id);
        }
    }
}