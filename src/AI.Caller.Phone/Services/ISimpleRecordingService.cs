using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services
{
    /// <summary>
    /// 简化录音服务接口
    /// </summary>
    public interface ISimpleRecordingService
    {
        /// <summary>
        /// 开始录音
        /// </summary>
        /// <param name="sipUsername">SIP用户名</param>
        /// <returns>是否成功开始录音</returns>
        Task<bool> StartRecordingAsync(string sipUsername);

        /// <summary>
        /// 停止录音
        /// </summary>
        /// <param name="sipUsername">SIP用户名</param>
        /// <returns>是否成功停止录音</returns>
        Task<bool> StopRecordingAsync(string sipUsername);

        /// <summary>
        /// 获取用户的录音列表
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>录音列表</returns>
        Task<List<Recording>> GetRecordingsAsync(int userId);

        /// <summary>
        /// 删除录音
        /// </summary>
        /// <param name="recordingId">录音ID</param>
        /// <param name="userId">用户ID</param>
        /// <returns>是否成功删除</returns>
        Task<bool> DeleteRecordingAsync(int recordingId, int userId);

        /// <summary>
        /// 检查是否启用自动录音
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>是否启用自动录音</returns>
        Task<bool> IsAutoRecordingEnabledAsync(int userId);

        /// <summary>
        /// 设置自动录音
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="enabled">是否启用</param>
        /// <returns>是否设置成功</returns>
        Task<bool> SetAutoRecordingAsync(int userId, bool enabled);

        /// <summary>
        /// 获取当前录音状态
        /// </summary>
        /// <param name="sipUsername">SIP用户名</param>
        /// <returns>录音状态</returns>
        Task<RecordingStatus?> GetRecordingStatusAsync(string sipUsername);
    }
}