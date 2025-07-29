using SIPSorcery.SIP;
using AI.Caller.Phone.CallRouting.Models;

namespace AI.Caller.Phone.CallRouting.Interfaces
{
    /// <summary>
    /// 通话路由服务接口
    /// </summary>
    public interface ICallRoutingService
    {
        /// <summary>
        /// 路由新呼入通话
        /// </summary>
        /// <param name="sipRequest">SIP请求</param>
        /// <returns>路由结果</returns>
        Task<CallRoutingResult> RouteInboundCallAsync(SIPRequest sipRequest);

        /// <summary>
        /// 路由呼出应答
        /// </summary>
        /// <param name="sipRequest">SIP请求</param>
        /// <returns>路由结果</returns>
        Task<CallRoutingResult> RouteOutboundResponseAsync(SIPRequest sipRequest);
    }
}