using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using PhoneRecordingStatus = AI.Caller.Phone.Entities.RecordingStatus;
using PhoneStorageInfo = AI.Caller.Phone.Models.StorageInfo;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 录音服务接口
/// </summary>
public interface IRecordingService
{
    /// <summary>
    /// 开始录音
    /// </summary>
    /// <param name="callId">通话ID</param>
    /// <param name="userId">用户ID</param>
    /// <param name="callerNumber">主叫号码</param>
    /// <param name="calleeNumber">被叫号码</param>
    /// <returns>录音操作结果</returns>
    Task<RecordingResult> StartRecordingAsync(string callId, int userId, string callerNumber, string calleeNumber);

    /// <summary>
    /// 停止录音
    /// </summary>
    /// <param name="callId">通话ID</param>
    /// <returns>录音操作结果</returns>
    Task<RecordingResult> StopRecordingAsync(string callId);

    /// <summary>
    /// 获取录音记录
    /// </summary>
    /// <param name="recordingId">录音ID</param>
    /// <returns>录音记录</returns>
    Task<CallRecording?> GetRecordingAsync(int recordingId);

    /// <summary>
    /// 获取用户的录音列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="filter">查询过滤器</param>
    /// <returns>分页的录音列表</returns>
    Task<PagedResult<CallRecording>> GetRecordingsAsync(int userId, RecordingFilter filter);

    /// <summary>
    /// 获取录音文件流
    /// </summary>
    /// <param name="recordingId">录音ID</param>
    /// <returns>文件流</returns>
    Task<Stream?> GetRecordingStreamAsync(int recordingId);

    /// <summary>
    /// 删除录音记录
    /// </summary>
    /// <param name="recordingId">录音ID</param>
    /// <param name="userId">用户ID</param>
    /// <returns>是否删除成功</returns>
    Task<bool> DeleteRecordingAsync(int recordingId, int userId);

    /// <summary>
    /// 获取存储信息
    /// </summary>
    /// <returns>存储使用情况</returns>
    Task<PhoneStorageInfo> GetStorageInfoAsync();

    /// <summary>
    /// 清理过期的录音记录
    /// </summary>
    /// <returns>清理的记录数量</returns>
    Task<int> CleanupExpiredRecordingsAsync();

    /// <summary>
    /// 获取录音状态
    /// </summary>
    /// <param name="callId">通话ID</param>
    /// <returns>录音状态</returns>
    Task<PhoneRecordingStatus?> GetRecordingStatusAsync(string callId);

    /// <summary>
    /// 检查用户是否有录音权限
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="recordingId">录音ID</param>
    /// <returns>是否有权限</returns>
    Task<bool> HasRecordingPermissionAsync(int userId, int recordingId);
}