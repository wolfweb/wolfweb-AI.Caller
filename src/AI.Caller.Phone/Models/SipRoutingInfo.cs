using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Models;

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
    
    /// <summary>
    /// 选择的线路
    /// </summary>
    public SipLine? SelectedLine { get; set; }
    
    /// <summary>
    /// 是否使用默认SIP服务器
    /// </summary>
    public bool IsUsingDefaultSipServer { get; set; }
}