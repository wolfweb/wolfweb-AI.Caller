using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Services {
    public class InboundTemplateService : IInboundTemplateService {
        private readonly AppDbContext _context;
        private readonly ILogger<InboundTemplateService> _logger;

        public InboundTemplateService(
            AppDbContext context,
            ILogger<InboundTemplateService> logger) {
            _context = context;
            _logger = logger;
        }

        public async Task<InboundTemplate> CreateTemplateAsync(InboundTemplate template) {
            template.CreatedTime = DateTime.UtcNow;
            
            _context.InboundTemplates.Add(template);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"呼入模板创建成功: {template.Name}");
            return template;
        }

        public async Task<InboundTemplate?> UpdateTemplateAsync(int templateId, InboundTemplate template) {
            var existingTemplate = await _context.InboundTemplates.FindAsync(templateId);
            if (existingTemplate == null) {
                return null;
            }

            existingTemplate.Name = template.Name;
            existingTemplate.Description = template.Description;
            existingTemplate.WelcomeScript = template.WelcomeScript;
            existingTemplate.ResponseRules = template.ResponseRules;
            existingTemplate.IsActive = template.IsActive;
            existingTemplate.UpdatedTime = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation($"呼入模板更新成功: {template.Name}");
            return existingTemplate;
        }

        public async Task<bool> DeleteTemplateAsync(int templateId) {
            var template = await _context.InboundTemplates.FindAsync(templateId);
            if (template == null) {
                return false;
            }

            if (template.IsDefault) {
                throw new InvalidOperationException("无法删除默认模板，请先设置其他模板为默认");
            }

            _context.InboundTemplates.Remove(template);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"呼入模板删除成功: {template.Name}");
            return true;
        }

        public async Task<List<InboundTemplate>> GetUserTemplatesAsync(int userId) {
            return await _context.InboundTemplates
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.IsDefault)
                .ThenByDescending(t => t.CreatedTime)
                .ToListAsync();
        }

        public async Task<InboundTemplate?> GetTemplateAsync(int templateId) {
            return await _context.InboundTemplates.FindAsync(templateId);
        }

        public async Task<bool> SetDefaultTemplateAsync(int templateId, int userId) {
            using var transaction = await _context.Database.BeginTransactionAsync();
            
            try {
                var currentDefaults = await _context.InboundTemplates
                    .Where(t => t.UserId == userId && t.IsDefault)
                    .ToListAsync();

                foreach (var template in currentDefaults) {
                    template.IsDefault = false;
                }

                var newDefault = await _context.InboundTemplates.FindAsync(templateId);
                if (newDefault == null || newDefault.UserId != userId) {
                    return false;
                }

                newDefault.IsDefault = true;
                newDefault.UpdatedTime = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation($"默认呼入模板设置成功: {newDefault.Name}");
                return true;
            } catch (Exception ex) {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"设置默认呼入模板失败: TemplateId={templateId}");
                throw;
            }
        }

        public async Task<InboundTemplate?> GetDefaultTemplateAsync(int userId) {
            return await _context.InboundTemplates
                .Where(t => t.UserId == userId && t.IsDefault && t.IsActive)
                .FirstOrDefaultAsync();
        }

        public async Task<List<InboundTemplate>> GetActiveTemplatesAsync(int userId) {
            return await _context.InboundTemplates
                .Where(t => t.UserId == userId && t.IsActive)
                .OrderByDescending(t => t.IsDefault)
                .ThenBy(t => t.Name)
                .ToListAsync();
        }
    }
}