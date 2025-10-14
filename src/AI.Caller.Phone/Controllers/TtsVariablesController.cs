using AI.Caller.Phone.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Controllers {
    [Authorize]
    public class TtsVariablesController : Controller {
        private readonly AppDbContext _context;

        public TtsVariablesController(AppDbContext context) {
            _context = context;
        }

        public async Task<IActionResult> Index() {
            var variables = await _context.TtsVariables
                                          .Include(v => v.TtsTemplates)
                                          .Select(v => new TtsVariable {
                                              Id = v.Id,
                                              Name = v.Name,
                                              Description = v.Description,
                                              TtsTemplates = v.TtsTemplates
                                          })
                                          .ToListAsync();
            return View(variables);
        }

        public IActionResult Create() {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Description")] TtsVariable ttsVariable) {
            if (ModelState.IsValid) {
                _context.Add(ttsVariable);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(ttsVariable);
        }

        public async Task<IActionResult> Edit(int? id) {
            if (id == null) {
                return NotFound();
            }

            var ttsVariable = await _context.TtsVariables.FindAsync(id);
            if (ttsVariable == null) {
                return NotFound();
            }
            return View(ttsVariable);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Description")] TtsVariable ttsVariable) {
            if (id != ttsVariable.Id) {
                return NotFound();
            }

            if (ModelState.IsValid) {
                try {
                    _context.Update(ttsVariable);
                    await _context.SaveChangesAsync();
                } catch (DbUpdateConcurrencyException) {
                    if (!TtsVariableExists(ttsVariable.Id)) {
                        return NotFound();
                    } else {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(ttsVariable);
        }

        public async Task<IActionResult> Delete(int? id) {
            if (id == null) {
                return NotFound();
            }

            var ttsVariable = await _context.TtsVariables
                .Include(v => v.TtsTemplates)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (ttsVariable == null) {
                return NotFound();
            }

            return View(ttsVariable);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id) {
            var ttsVariable = await _context.TtsVariables.FindAsync(id);
            _context.TtsVariables.Remove(ttsVariable);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool TtsVariableExists(int id) {
            return _context.TtsVariables.Any(e => e.Id == id);
        }
    }
}