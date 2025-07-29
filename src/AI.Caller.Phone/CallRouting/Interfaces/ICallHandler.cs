using SIPSorcery.SIP;
using AI.Caller.Phone.CallRouting.Models;

namespace AI.Caller.Phone.CallRouting.Interfaces
{
    /// <summary>
    /// 通话处理器接口
    /// </summary>
    public interface ICallHandler
    {
        /// <summary>
        /// 处理通话
        /// </summary>
        /// <param name="sipRequest">SIP请求</param>
        /// <param name="routingResult">路由结果</param>
        /// <returns>处理是否成功</returns>
        Task<bool> HandleCallAsync(SIPRequest sipRequest, CallRoutingResult routingResult);
    }
}