namespace AI.Caller.Core.Models;

/// <summary>
/// SIP路由信息，包含呼叫所需的服务器配置
/// </summary>
public class SipRoutingInfo {
    /// <summary>
    /// 代理服务器地址
    /// </summary>
    public string ProxyServer { get; set; } = string.Empty;
    
    /// <summary>
    /// 出站代理（可选）
    /// </summary>
    public string? OutboundProxy { get; set; }
}