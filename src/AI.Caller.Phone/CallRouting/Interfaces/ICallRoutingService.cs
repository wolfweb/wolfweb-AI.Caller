using SIPSorcery.SIP;
using AI.Caller.Phone.CallRouting.Models;

namespace AI.Caller.Phone.CallRouting.Interfaces {
    public interface ICallRoutingService {
        /// <summary>
        /// 路由新呼入通话
        /// </summary>
        /// <param name="sipRequest">SIP请求</param>
        /// <returns>路由结果</returns>
        Task<CallRoutingResult> RouteInboundCallAsync(string toUser, SIPRequest sipRequest);
    }
}