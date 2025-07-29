using SIPSorcery.SIP;
using AI.Caller.Phone.CallRouting.Models;

namespace AI.Caller.Phone.CallRouting.Interfaces
{
    /// <summary>
    /// 来电类型识别器接口
    /// </summary>
    public interface ICallTypeIdentifier
    {
        /// <summary>
        /// 识别来电类型
        /// </summary>
        /// <param name="sipRequest">SIP请求</param>
        /// <returns>来电类型</returns>
        CallType IdentifyCallType(SIPRequest sipRequest);

        /// <summary>
        /// 注册呼出通话
        /// </summary>
        /// <param name="callId">通话ID</param>
        /// <param name="sipUsername">SIP用户名</param>
        /// <param name="destination">目标号码</param>
        void RegisterOutboundCall(string callId, string sipUsername, string destination);

        /// <summary>
        /// 注册呼出通话（使用SIP标签）
        /// </summary>
        /// <param name="callId">通话ID</param>
        /// <param name="fromTag">From标签</param>
        /// <param name="sipUsername">SIP用户名</param>
        /// <param name="destination">目标号码</param>
        void RegisterOutboundCallWithSipTags(string callId, string fromTag, string sipUsername, string destination);

        /// <summary>
        /// 注销呼出通话
        /// </summary>
        /// <param name="callId">通话ID</param>
        void UnregisterOutboundCall(string callId);

        /// <summary>
        /// 获取呼出通话信息
        /// </summary>
        /// <param name="callId">通话ID</param>
        /// <returns>呼出通话信息</returns>
        OutboundCallInfo? GetOutboundCallInfo(string callId);

        /// <summary>
        /// 注册呼出通话（增强版本，包含更多上下文信息）
        /// </summary>
        /// <param name="callId">通话ID</param>
        /// <param name="sipUsername">SIP用户名</param>
        /// <param name="destination">目标号码</param>
        /// <param name="originalFromUri">原始From URI</param>
        /// <param name="originalToUri">原始To URI</param>
        void RegisterOutboundCallWithContext(string callId, string sipUsername, string destination, string? originalFromUri = null, string? originalToUri = null);

        /// <summary>
        /// 根据多个条件查找呼出通话（用于处理号码转换情况）
        /// </summary>
        /// <param name="sipRequest">SIP请求</param>
        /// <returns>匹配的呼出通话信息</returns>
        OutboundCallInfo? FindOutboundCallByContext(SIPRequest sipRequest);

        /// <summary>
        /// 更新呼出通话状态（外部调用）
        /// </summary>
        /// <param name="callId">通话ID</param>
        /// <param name="status">新状态</param>
        void UpdateOutboundCallStatus(string callId, CallStatus status);

        /// <summary>
        /// 获取所有活跃的呼出通话
        /// </summary>
        /// <returns>活跃的呼出通话列表</returns>
        IEnumerable<OutboundCallInfo> GetActiveOutboundCalls();

        /// <summary>
        /// 获取呼出通话统计信息
        /// </summary>
        /// <returns>统计信息元组</returns>
        (int Total, int Active, int Answered, int Failed) GetOutboundCallStats();
    }
}