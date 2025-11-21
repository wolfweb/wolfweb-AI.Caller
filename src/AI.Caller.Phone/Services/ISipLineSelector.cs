using System.Threading.Tasks;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services {
    /// <summary>
    /// SIP线路选择器接口
    /// </summary>
    public interface ISipLineSelector {
        /// <summary>
        /// 根据SIP账户和选择策略选择路由信息
        /// </summary>
        /// <param name="sipAccount">SIP账户</param>
        /// <param name="preferredLineId">首选线路ID（可选）</param>
        /// <param name="autoSelectLine">是否自动选择线路</param>
        /// <returns>路由信息</returns>
        Task<SipRoutingInfo> SelectRoutingAsync(SipAccount sipAccount, int? preferredLineId, bool autoSelectLine = true);
    }
}