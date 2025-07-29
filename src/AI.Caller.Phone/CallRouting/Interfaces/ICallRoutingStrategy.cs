using AI.Caller.Core;

namespace AI.Caller.Phone.CallRouting.Interfaces
{
    /// <summary>
    /// 通话路由策略接口
    /// </summary>
    public interface ICallRoutingStrategy
    {
        /// <summary>
        /// 策略名称
        /// </summary>
        string StrategyName { get; }

        /// <summary>
        /// 选择客户端
        /// </summary>
        /// <param name="targetSipUsername">目标SIP用户名</param>
        /// <param name="availableClients">可用客户端列表</param>
        /// <returns>选中的客户端</returns>
        Task<SIPClient?> SelectClientAsync(string targetSipUsername, IEnumerable<SIPClient> availableClients);
    }
}