using AI.Caller.Phone.CallRouting.Models;
using SIPSorcery.SIP;

namespace AI.Caller.Phone.CallRouting.Interfaces {
    /// <summary>
    /// 呼叫类型识别接口
    /// </summary>
    public interface ICallTypeIdentifier {
        /// <summary>
        /// 获取呼出通话信息
        /// </summary>
        /// <param name="callId">呼叫ID</param>
        /// <returns>呼出通话信息</returns>
        OutboundCallInfo? GetOutboundCallInfo(string callId);

        /// <summary>
        /// 注册呼出通话
        /// </summary>
        /// <param name="callId">呼叫ID</param>
        /// <param name="fromTag">From标签</param>
        /// <param name="sipUsername">SIP用户名</param>
        /// <param name="destination">目标号码</param>
        void RegisterOutboundCallWithSipTags(string callId, string fromTag, string sipUsername, string destination);

        /// <summary>
        /// 更新呼出通话状态
        /// </summary>
        /// <param name="callId">呼叫ID</param>
        /// <param name="status">新状态</param>
        void UpdateOutboundCallStatus(string callId, CallStatus status);

        /// <summary>
        /// 获取活跃的呼出通话列表
        /// </summary>
        /// <returns>活跃的呼出通话列表</returns>
        IEnumerable<OutboundCallInfo> GetActiveOutboundCalls();

        /// <summary>
        /// 清理已结束的呼叫记录
        /// </summary>
        void CleanupEndedCalls();
    }
}