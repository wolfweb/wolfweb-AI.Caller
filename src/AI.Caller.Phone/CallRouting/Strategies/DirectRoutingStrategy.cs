using Microsoft.Extensions.Logging;
using AI.Caller.Core;
using AI.Caller.Phone.CallRouting.Interfaces;

namespace AI.Caller.Phone.CallRouting.Strategies
{
    /// <summary>
    /// 直接路由策略实现
    /// </summary>
    public class DirectRoutingStrategy : ICallRoutingStrategy
    {
        private readonly ILogger<DirectRoutingStrategy> _logger;

        public string StrategyName => "Direct";

        public DirectRoutingStrategy(ILogger<DirectRoutingStrategy> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 选择客户端
        /// </summary>
        public async Task<SIPClient?> SelectClientAsync(string targetSipUsername, IEnumerable<SIPClient> availableClients)
        {
            try
            {
                _logger.LogDebug($"开始直接路由选择 - 目标用户: {targetSipUsername}, 可用客户端数量: {availableClients.Count()}");

                // 1. 首先尝试精确匹配目标用户名
                var exactMatch = availableClients.FirstOrDefault(client => 
                    IsClientForUser(client, targetSipUsername));

                if (exactMatch != null)
                {
                    _logger.LogInformation($"找到精确匹配的客户端 - 用户: {targetSipUsername}");
                    return exactMatch;
                }

                // 2. 如果没有精确匹配，选择第一个可用的客户端
                var firstAvailable = availableClients.FirstOrDefault(client => 
                    IsClientAvailable(client));

                if (firstAvailable != null)
                {
                    _logger.LogInformation($"使用第一个可用客户端作为备选 - 目标用户: {targetSipUsername}");
                    return firstAvailable;
                }

                _logger.LogWarning($"未找到可用的客户端 - 目标用户: {targetSipUsername}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"直接路由选择失败 - 目标用户: {targetSipUsername}");
                return null;
            }
        }

        /// <summary>
        /// 检查客户端是否属于指定用户
        /// </summary>
        private bool IsClientForUser(SIPClient client, string targetSipUsername)
        {
            try
            {
                // 通过客户端ID判断是否属于目标用户
                var clientId = client.GetClientId();
                if (string.IsNullOrEmpty(clientId))
                    return false;

                // 客户端ID通常包含用户名信息
                return clientId.Contains(targetSipUsername, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查客户端用户归属时发生错误");
                return false;
            }
        }

        /// <summary>
        /// 检查客户端是否可用
        /// </summary>
        private bool IsClientAvailable(SIPClient client)
        {
            try
            {
                // 检查客户端是否正在通话中
                if (client.IsCallActive)
                {
                    _logger.LogDebug($"客户端正在通话中 - ClientId: {client.GetClientId()}");
                    return false;
                }

                // 可以添加更多的可用性检查
                // 例如：网络连接状态、资源使用情况等

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查客户端可用性时发生错误");
                return false;
            }
        }
    }
}