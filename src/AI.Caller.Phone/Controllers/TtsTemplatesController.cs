using AI.Caller.Phone.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using AI.Caller.Core.Media;
using System.Text;
using AI.Caller.Core.Models;

namespace AI.Caller.Phone.Controllers {
    [Authorize]
    public class TtsTemplatesController : Controller {
        private readonly AppDbContext _context;
        private readonly ITTSEngine _ttsEngine;

        public TtsTemplatesController(AppDbContext context, ITTSEngine ttsEngine) {
            _context = context;
            _ttsEngine = ttsEngine;
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
        public async Task<IActionResult> Create([Bind("Name,Content,IsActive,PlayCount,HangupAfterPlay,PauseBetweenPlaysInSeconds,EndingSpeech,SpeechRate")] TtsTemplate ttsTemplate, int[] selectedVariables) {
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
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Content,IsActive,PlayCount,HangupAfterPlay,PauseBetweenPlaysInSeconds,EndingSpeech,SpeechRate")] TtsTemplate ttsTemplate, int[] selectedVariables) {
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
                    templateToUpdate.PauseBetweenPlaysInSeconds = ttsTemplate.PauseBetweenPlaysInSeconds;
                    templateToUpdate.EndingSpeech = ttsTemplate.EndingSpeech;
                    templateToUpdate.SpeechRate = ttsTemplate.SpeechRate;

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

        [HttpPost]
        public async Task<IActionResult> TestTts([FromBody] TtsTestRequest request) {
            if (string.IsNullOrWhiteSpace(request.Text)) {
                return BadRequest("Text cannot be empty.");
            }

            var audioStream = new MemoryStream();
            await foreach (var audioData in _ttsEngine.SynthesizeStreamAsync(request.Text, 0, (float)request.SpeechRate)) {
                if (audioData.Format == AudioDataFormat.PCM_Float) {
                    // Convert float to 16-bit PCM
                    var pcm16 = new short[audioData.FloatData.Length];
                    for (int i = 0; i < audioData.FloatData.Length; i++) {
                        pcm16[i] = (short)(audioData.FloatData[i] * 32767);
                    }
                    var byteData = new byte[pcm16.Length * 2];
                    Buffer.BlockCopy(pcm16, 0, byteData, 0, byteData.Length);
                    await audioStream.WriteAsync(byteData, 0, byteData.Length);
                }
            }

            if (audioStream.Length == 0) {
                return new EmptyResult();
            }

            var wavStream = new MemoryStream();
            WriteWavHeader(wavStream, (int)audioStream.Length, 1, 16000, 16);
            audioStream.Position = 0;
            await audioStream.CopyToAsync(wavStream);

            wavStream.Position = 0;
            return new FileContentResult(wavStream.ToArray(), "audio/wav");
        }

        private void WriteWavHeader(Stream stream, int dataLength, int numChannels, int sampleRate, int bitsPerSample) {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) {
                writer.Write(Encoding.UTF8.GetBytes("RIFF"));
                writer.Write(36 + dataLength);
                writer.Write(Encoding.UTF8.GetBytes("WAVE"));
                writer.Write(Encoding.UTF8.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1); // Audio format 1=PCM
                writer.Write((short)numChannels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * numChannels * (bitsPerSample / 8));
                writer.Write((short)(numChannels * (bitsPerSample / 8)));
                writer.Write((short)bitsPerSample);
                writer.Write(Encoding.UTF8.GetBytes("data"));
                writer.Write(dataLength);
            }
        }
    }

    public class TtsTestRequest {
        public string Text { get; set; }
        public double SpeechRate { get; set; }
    }
}