using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using System.Text.RegularExpressions;

namespace AI.Caller.Phone.Services {
    public class TtsCallDocumentService : ITtsCallDocumentService {
        private readonly AppDbContext _context;
        private readonly ILogger<TtsCallDocumentService> _logger;
        private readonly IFileStorageService _fileStorage;
        private readonly ITtsCallTaskService _taskService;

        public TtsCallDocumentService(
            AppDbContext context,
            ILogger<TtsCallDocumentService> logger,
            IFileStorageService fileStorage,
            ITtsCallTaskService taskService) {
            _context = context;
            _logger = logger;
            _fileStorage = fileStorage;
            _taskService = taskService;
        }

        public async Task<byte[]> GenerateTemplateAsync() {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("TTS外呼模板");
            
            // 设置表头
            worksheet.Cell(1, 1).Value = "手机号";
            worksheet.Cell(1, 2).Value = "性别";
            worksheet.Cell(1, 3).Value = "称呼模板";
            worksheet.Cell(1, 4).Value = "TTS内容";
            
            // 设置表头样式
            var headerRange = worksheet.Range(1, 1, 1, 4);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            
            // 添加示例数据
            worksheet.Cell(2, 1).Value = "13800138000";
            worksheet.Cell(2, 2).Value = "男";
            worksheet.Cell(2, 3).Value = "先生";
            worksheet.Cell(2, 4).Value = "您好，{称呼}，这里是XX公司，有重要事项需要与您沟通...";
            
            worksheet.Cell(3, 1).Value = "13900139000";
            worksheet.Cell(3, 2).Value = "女";
            worksheet.Cell(3, 3).Value = "女士";
            worksheet.Cell(3, 4).Value = "您好，{称呼}，这里是XX公司，有重要事项需要与您沟通...";
            
            // 自动调整列宽
            worksheet.Columns().AdjustToContents();
            
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public async Task<TtsCallDocument> UploadDocumentAsync(IFormFile file, int userId) {
            if (file == null || file.Length == 0) {
                throw new ArgumentException("文件不能为空");
            }

            if (!IsValidFileType(file.FileName)) {
                throw new ArgumentException("只支持Excel文件格式(.xlsx, .xls)");
            }

            // 保存文件
            var filePath = await _fileStorage.SaveFileAsync(file, "tts-documents");
            
            try {
                // 解析Excel文件
                var records = await ParseExcelFileAsync(file);
                
                // 创建文档记录
                var document = new TtsCallDocument {
                    FileName = file.FileName,
                    FilePath = filePath,
                    TotalRecords = records.Count,
                    UserId = userId,
                    CallRecords = records
                };

                _context.TtsCallDocuments.Add(document);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"TTS外呼文档上传成功: {file.FileName}, 记录数: {records.Count}");
                
                return document;
            } catch (Exception ex) {
                // 删除已保存的文件
                await _fileStorage.DeleteFileAsync(filePath);
                _logger.LogError(ex, $"解析TTS外呼文档失败: {file.FileName}");
                throw;
            }
        }

        public async Task<bool> StartCallTaskAsync(int documentId) {
            var document = await _context.TtsCallDocuments
                .Include(d => d.CallRecords)
                .FirstOrDefaultAsync(d => d.Id == documentId);

            if (document == null) {
                return false;
            }

            if (document.Status == TtsCallTaskStatus.Running) {
                return false; // 任务已在运行
            }

            document.Status = TtsCallTaskStatus.Running;
            document.StartTime = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();

            // 启动后台任务
            _ = Task.Run(() => _taskService.ExecuteCallTaskAsync(documentId));

            _logger.LogInformation($"TTS外呼任务启动: DocumentId={documentId}");
            return true;
        }

        public async Task<bool> PauseCallTaskAsync(int documentId) {
            var document = await _context.TtsCallDocuments.FindAsync(documentId);
            if (document == null || document.Status != TtsCallTaskStatus.Running) {
                return false;
            }

            document.Status = TtsCallTaskStatus.Paused;
            await _context.SaveChangesAsync();

            await _taskService.PauseTaskAsync(documentId);
            
            _logger.LogInformation($"TTS外呼任务暂停: DocumentId={documentId}");
            return true;
        }

        public async Task<bool> ResumeCallTaskAsync(int documentId) {
            var document = await _context.TtsCallDocuments.FindAsync(documentId);
            if (document == null || document.Status != TtsCallTaskStatus.Paused) {
                return false;
            }

            document.Status = TtsCallTaskStatus.Running;
            await _context.SaveChangesAsync();

            await _taskService.ResumeTaskAsync(documentId);
            
            _logger.LogInformation($"TTS外呼任务恢复: DocumentId={documentId}");
            return true;
        }

        public async Task<bool> StopCallTaskAsync(int documentId) {
            var document = await _context.TtsCallDocuments.FindAsync(documentId);
            if (document == null) {
                return false;
            }

            document.Status = TtsCallTaskStatus.Cancelled;
            document.EndTime = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _taskService.StopTaskAsync(documentId);
            
            _logger.LogInformation($"TTS外呼任务停止: DocumentId={documentId}");
            return true;
        }

        public async Task<List<TtsCallDocument>> GetUserDocumentsAsync(int userId) {
            return await _context.TtsCallDocuments
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.UploadTime)
                .ToListAsync();
        }

        public async Task<TtsCallDocument?> GetDocumentAsync(int documentId) {
            return await _context.TtsCallDocuments
                .Include(d => d.CallRecords)
                .FirstOrDefaultAsync(d => d.Id == documentId);
        }

        public async Task<bool> DeleteDocumentAsync(int documentId) {
            var document = await _context.TtsCallDocuments.FindAsync(documentId);
            if (document == null) {
                return false;
            }

            if (document.Status == TtsCallTaskStatus.Running) {
                throw new InvalidOperationException("无法删除正在运行的任务");
            }

            // 删除文件
            await _fileStorage.DeleteFileAsync(document.FilePath);
            
            // 删除数据库记录
            _context.TtsCallDocuments.Remove(document);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"TTS外呼文档删除: DocumentId={documentId}");
            return true;
        }

        private async Task<List<TtsCallRecord>> ParseExcelFileAsync(IFormFile file) {
            var records = new List<TtsCallRecord>();
            
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null) {
                throw new ArgumentException("Excel文件中没有找到工作表");
            }

            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            if (lastRow < 2) {
                throw new ArgumentException("Excel文件中没有数据行");
            }

            for (int row = 2; row <= lastRow; row++) {
                var phoneNumber = worksheet.Cell(row, 1).GetString().Trim();
                var gender = worksheet.Cell(row, 2).GetString().Trim();
                var addressTemplate = worksheet.Cell(row, 3).GetString().Trim();
                var ttsContent = worksheet.Cell(row, 4).GetString().Trim();

                // 验证数据
                if (string.IsNullOrEmpty(phoneNumber)) {
                    throw new ArgumentException($"第{row}行手机号不能为空");
                }

                //if (!IsValidPhoneNumber(phoneNumber)) {
                //    throw new ArgumentException($"第{row}行手机号格式不正确: {phoneNumber}");
                //}

                if (string.IsNullOrEmpty(ttsContent)) {
                    throw new ArgumentException($"第{row}行TTS内容不能为空");
                }

                records.Add(new TtsCallRecord {
                    PhoneNumber = phoneNumber,
                    Gender = gender ?? "",
                    AddressTemplate = addressTemplate ?? "",
                    TtsContent = ttsContent
                });
            }

            return records;
        }

        private bool IsValidFileType(string fileName) {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension == ".xlsx" || extension == ".xls";
        }

        private bool IsValidPhoneNumber(string phoneNumber) {
            // 中国手机号正则表达式
            var regex = new Regex(@"^1[3-9]\d{9}$");
            return regex.IsMatch(phoneNumber);
        }
    }
}