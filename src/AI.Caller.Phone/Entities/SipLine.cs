using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AI.Caller.Phone.Entities;

/// <summary>
/// SIP线路实体，用于管理不同的SIP代理服务器和路由配置
/// </summary>
public class SipLine {
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;        // "北京电信"、"上海联通"

    [Required]
    [StringLength(200)]
    public string ProxyServer { get; set; } = string.Empty; // 代理服务器地址

    [StringLength(200)]
    public string? OutboundProxy { get; set; }               // 出站代理（可选）

    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 0;                  // 优先级

    [StringLength(50)]
    public string? Region { get; set; }                      // 区域标识

    [StringLength(500)]
    public string? Description { get; set; }                 // 描述

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<SipAccount> SipAccounts { get; set; } = new List<SipAccount>();
}