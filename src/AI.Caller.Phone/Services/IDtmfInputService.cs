using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services;

/// <summary>
/// DTMF输入服务接口
/// </summary>
public interface IDtmfInputService {
    /// <summary>
    /// 获取DTMF输入模板
    /// </summary>
    Task<DtmfInputTemplate?> GetTemplateAsync(int id);

    /// <summary>
    /// 获取所有DTMF输入模板
    /// </summary>
    Task<List<DtmfInputTemplate>> GetAllTemplatesAsync();

    /// <summary>
    /// 创建DTMF输入模板
    /// </summary>
    Task<DtmfInputTemplate> CreateTemplateAsync(DtmfInputTemplate template);

    /// <summary>
    /// 更新DTMF输入模板
    /// </summary>
    Task UpdateTemplateAsync(DtmfInputTemplate template);

    /// <summary>
    /// 删除DTMF输入模板
    /// </summary>
    Task DeleteTemplateAsync(int id);

    /// <summary>
    /// 记录DTMF输入
    /// </summary>
    Task<DtmfInputRecord> RecordInputAsync(DtmfInputRecord record);

    /// <summary>
    /// 获取通话的所有DTMF输入记录
    /// </summary>
    Task<List<DtmfInputRecord>> GetCallInputRecordsAsync(string callId);

    /// <summary>
    /// 验证DTMF输入
    /// </summary>
    Task<(bool IsValid, string? ErrorMessage)> ValidateInputAsync(int templateId, string input);
}
