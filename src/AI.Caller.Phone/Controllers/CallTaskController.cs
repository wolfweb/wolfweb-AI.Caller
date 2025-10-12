using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Security.Claims;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace AI.Caller.Phone.Controllers {
    public class CallTaskController : Controller {
        private readonly AppDbContext _context;
        private readonly ICallTaskService _callTaskService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IBackgroundTaskQueue _taskQueue;

        public CallTaskController(AppDbContext context, ICallTaskService callTaskService, IWebHostEnvironment webHostEnvironment, IBackgroundTaskQueue taskQueue) {
            _context = context;
            _callTaskService = callTaskService;
            _webHostEnvironment = webHostEnvironment;
            _taskQueue = taskQueue;
        }

        public async Task<IActionResult> Index() {
            var batchJobs = await _context.BatchCallJobs.OrderByDescending(j => j.CreatedAt).ToListAsync();
            return View(batchJobs);
        }

        public async Task<IActionResult> Details(int id) {
            var batchJob = await _context.BatchCallJobs
                .Include(j => j.CallLogs)
                .FirstOrDefaultAsync(j => j.Id == id);

            if (batchJob == null) {
                return NotFound();
            }

            return View(batchJob);
        }

        public async Task<IActionResult> CreateBatch() {
            ViewData["TtsTemplateId"] = new SelectList(await _context.TtsTemplates.Where(t => t.IsActive).ToListAsync(), "Id", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBatch(string jobName, int ttsTemplateId, IFormFile file) {
            if (file == null || file.Length == 0) {
                ModelState.AddModelError("file", "Please select a file to upload.");
                ViewData["TtsTemplateId"] = new SelectList(await _context.TtsTemplates.Where(t => t.IsActive).ToListAsync(), "Id", "Name", ttsTemplateId);
                return View();
            }

            var uploadsRootFolder = Path.Combine(_webHostEnvironment.ContentRootPath, "uploads");
            if (!Directory.Exists(uploadsRootFolder)) {
                Directory.CreateDirectory(uploadsRootFolder);
            }

            var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsRootFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create)) {
                await file.CopyToAsync(stream);
            }

            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            await _callTaskService.CreateBatchCallTaskAsync(jobName, ttsTemplateId, filePath, file.FileName, userId);

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pause(int id) {
            var batchJob = await _context.BatchCallJobs.FindAsync(id);
            if (batchJob != null && batchJob.Status == BatchJobStatus.Processing) {
                batchJob.Status = BatchJobStatus.Paused;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Resume(int id) {
            var batchJob = await _context.BatchCallJobs.FindAsync(id);
            if (batchJob != null && batchJob.Status == BatchJobStatus.Paused) {
                batchJob.Status = BatchJobStatus.Processing;
                await _context.SaveChangesAsync();

                var queuedLogs = await _context.CallLogs
                    .Where(l => l.BatchCallJobId == id && l.Status == Entities.CallStatus.Queued)
                    .ToListAsync();

                foreach (var callLog in queuedLogs) {
                    _taskQueue.QueueBackgroundWorkItem((token, serviceProvider) => {
                        var callProcessor = serviceProvider.GetRequiredService<ICallProcessor>();
                        return callProcessor.ProcessCallLogJob(callLog.Id);
                    });
                }
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id) {
            var batchJob = await _context.BatchCallJobs.FindAsync(id);
            if (batchJob != null && (batchJob.Status == BatchJobStatus.Processing || batchJob.Status == BatchJobStatus.Paused || batchJob.Status == BatchJobStatus.Queued)) {
                batchJob.Status = BatchJobStatus.Cancelled;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RetryFailed(int id) {
            var batchJob = await _context.BatchCallJobs.FindAsync(id);
            if (batchJob == null) {
                return NotFound();
            }

            var failedLogs = await _context.CallLogs
                .Where(l => l.BatchCallJobId == id && l.Status != Entities.CallStatus.Completed)
                .ToListAsync();

            if (failedLogs.Any()) {
                foreach (var log in failedLogs) {
                    log.Status = Entities.CallStatus.Queued;
                    log.FailureReason = null;
                    log.CompletedAt = null;
                }

                batchJob.Status = BatchJobStatus.Processing;
                batchJob.CompletedAt = null;

                await _context.SaveChangesAsync();

                foreach (var callLog in failedLogs) {
                    _taskQueue.QueueBackgroundWorkItem((token, serviceProvider) => {
                        var callProcessor = serviceProvider.GetRequiredService<ICallProcessor>();
                        return callProcessor.ProcessCallLogJob(callLog.Id);
                    });
                }
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id) {
            var batchJob = await _context.BatchCallJobs
                .Include(j => j.CallLogs)
                .FirstOrDefaultAsync(j => j.Id == id);

            if (batchJob == null) {
                return NotFound();
            }

            _context.CallLogs.RemoveRange(batchJob.CallLogs);
            _context.BatchCallJobs.Remove(batchJob);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("api/templates/{templateId}/excel-template")]
        public async Task<IActionResult> DownloadExcelTemplate(int templateId) {
            var template = await _context.TtsTemplates
                .Include(t => t.Variables)
                .FirstOrDefaultAsync(t => t.Id == templateId);

            if (template == null) {
                return NotFound();
            }

            var headers = new List<string> { "PhoneNumber" };
            headers.AddRange(template.Variables.Select(v => v.Name));

            using (var memoryStream = new MemoryStream()) {
                IWorkbook workbook = new XSSFWorkbook();
                ISheet sheet = workbook.CreateSheet("Template");
                IRow headerRow = sheet.CreateRow(0);

                for (int i = 0; i < headers.Count; i++) {
                    headerRow.CreateCell(i).SetCellValue(headers[i]);
                }

                workbook.Write(memoryStream, true);
                var fileBytes = memoryStream.ToArray();
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"template_{template.Name}.xlsx");
            }
        }
    }
}