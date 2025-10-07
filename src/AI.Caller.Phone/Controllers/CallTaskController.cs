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

        public CallTaskController(AppDbContext context, ICallTaskService callTaskService, IWebHostEnvironment webHostEnvironment) {
            _context = context;
            _callTaskService = callTaskService;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<IActionResult> Index() {
            var callLogs = _context.CallLogs.Include(c => c.BatchCallJob).OrderByDescending(c => c.CreatedAt);
            return View(await callLogs.ToListAsync());
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