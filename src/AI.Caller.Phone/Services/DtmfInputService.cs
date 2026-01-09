using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Phone.Services;

/// <summary>
/// DTMF输入服务实现
/// </summary>
public class DtmfInputService : IDtmfInputService {
    private readonly AppDbContext _dbContext;
    private readonly ILogger<DtmfInputService> _logger;
    private readonly Dictionary<string, IDtmfInputValidator> _validators;

    public DtmfInputService(
        AppDbContext dbContext,
        ILogger<DtmfInputService> logger) {
        _dbContext = dbContext;
        _logger = logger;

        // 初始化验证器
        _validators = new Dictionary<string, IDtmfInputValidator> {
            ["Numeric"] = new NumericValidator(),
            ["PhoneNumber"] = new PhoneNumberValidator(),
            ["IdCard"] = new IdCardValidator(),
            ["Date"] = new DateValidator(),
            ["MenuOption"] = new MenuOptionValidator()
        };
    }

    public async Task<DtmfInputTemplate?> GetTemplateAsync(int id) {
        try {
            return await _dbContext.DtmfInputTemplates
                .FirstOrDefaultAsync(t => t.Id == id);
        } catch (Exception ex) {
            _logger.LogError(ex, "获取DTMF模板失败: {TemplateId}", id);
            throw;
        }
    }

    public async Task<List<DtmfInputTemplate>> GetAllTemplatesAsync() {
        try {
            return await _dbContext.DtmfInputTemplates
                .OrderBy(t => t.Name)
                .ToListAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "获取DTMF模板列表失败");
            throw;
        }
    }

    public async Task<DtmfInputTemplate> CreateTemplateAsync(DtmfInputTemplate template) {
        try {
            template.CreatedAt = DateTime.UtcNow;
            template.UpdatedAt = DateTime.UtcNow;

            _dbContext.DtmfInputTemplates.Add(template);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("DTMF模板已创建: {TemplateId}, {Name}",
                template.Id, template.Name);

            return template;
        } catch (Exception ex) {
            _logger.LogError(ex, "创建DTMF模板失败: {Name}", template.Name);
            throw;
        }
    }

    public async Task UpdateTemplateAsync(DtmfInputTemplate template) {
        try {
            var existing = await _dbContext.DtmfInputTemplates
                .FirstOrDefaultAsync(t => t.Id == template.Id);

            if (existing == null) {
                throw new DtmfTemplateNotFoundException(template.Id);
            }

            existing.Name = template.Name;
            existing.InputType = template.InputType;
            existing.ValidatorType = template.ValidatorType;
            existing.MaxLength = template.MaxLength;
            existing.MinLength = template.MinLength;
            existing.TerminationKey = template.TerminationKey;
            existing.BackspaceKey = template.BackspaceKey;
            existing.PromptText = template.PromptText;
            existing.SuccessText = template.SuccessText;
            existing.ErrorText = template.ErrorText;
            existing.TimeoutText = template.TimeoutText;
            existing.MaxRetries = template.MaxRetries;
            existing.TimeoutSeconds = template.TimeoutSeconds;
            existing.InputMappingJson = template.InputMappingJson;
            existing.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("DTMF模板已更新: {TemplateId}", template.Id);
        } catch (DtmfTemplateNotFoundException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "更新DTMF模板失败: {TemplateId}", template.Id);
            throw;
        }
    }

    public async Task DeleteTemplateAsync(int id) {
        try {
            var template = await _dbContext.DtmfInputTemplates
                .FirstOrDefaultAsync(t => t.Id == id);

            if (template == null) {
                throw new DtmfTemplateNotFoundException(id);
            }

            _dbContext.DtmfInputTemplates.Remove(template);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("DTMF模板已删除: {TemplateId}", id);
        } catch (DtmfTemplateNotFoundException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "删除DTMF模板失败: {TemplateId}", id);
            throw;
        }
    }

    public async Task<DtmfInputRecord> RecordInputAsync(DtmfInputRecord record) {
        try {
            record.InputTime = DateTime.UtcNow;

            _dbContext.DtmfInputRecords.Add(record);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("DTMF输入已记录: {RecordId}, CallId: {CallId}",
                record.Id, record.CallId);

            return record;
        } catch (Exception ex) {
            _logger.LogError(ex, "记录DTMF输入失败: CallId {CallId}", record.CallId);
            throw;
        }
    }

    public async Task<List<DtmfInputRecord>> GetCallInputRecordsAsync(string callId) {
        try {
            return await _dbContext.DtmfInputRecords
                .Where(r => r.CallId == callId)
                .Include(r => r.Template)
                .Include(r => r.Segment)
                .OrderBy(r => r.InputTime)
                .ToListAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "获取通话DTMF记录失败: {CallId}", callId);
            throw;
        }
    }

    public async Task<(bool IsValid, string? ErrorMessage)> ValidateInputAsync(int templateId, string input) {
        try {
            var template = await GetTemplateAsync(templateId);
            if (template == null) {
                throw new DtmfTemplateNotFoundException(templateId);
            }

            // 获取验证器
            if (!_validators.TryGetValue(template.ValidatorType, out var validator)) {
                _logger.LogWarning("未找到验证器: {ValidatorType}", template.ValidatorType);
                return (true, null); // 如果没有验证器，默认通过
            }

            // 执行验证
            var result = validator.Validate(input, template);

            if (!result.IsValid) {
                _logger.LogWarning("DTMF输入验证失败: {TemplateId}, {ErrorMessage}",
                    templateId, result.ErrorMessage);
            }

            return result;
        } catch (DtmfTemplateNotFoundException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "验证DTMF输入失败: {TemplateId}", templateId);
            throw;
        }
    }
}
