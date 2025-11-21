using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Phone.Services;

/// <summary>
/// SIP线路选择器实现
/// </summary>
public class SipLineSelector : ISipLineSelector {
    private readonly AppDbContext _db;
    private readonly ILogger _logger;

    public SipLineSelector(AppDbContext db, ILogger<SipLineSelector> logger) {
        _db = db;
        _logger = logger;
    }

    public async Task<SipRoutingInfo> SelectRoutingAsync(
    SipAccount sipAccount,
    int? preferredLineId,
    bool autoSelectLine = true
    ) {
        if (sipAccount == null) throw new ArgumentNullException(nameof(sipAccount));

        await _db.Entry(sipAccount).Collection(a => a.AvailableLines).LoadAsync();

        SipLine? selected = null;

        if (preferredLineId.HasValue) {
            selected = sipAccount.AvailableLines.FirstOrDefault(l => l.Id == preferredLineId.Value && l.IsActive);
            if (selected == null) {
                selected = await _db.SipLines.FirstOrDefaultAsync(l => l.Id == preferredLineId.Value && l.IsActive);
            }

            if (selected != null) {
                _logger.LogDebug($"使用首选线路 {selected.Name} ({selected.Id}) 为账户 {sipAccount.Id} 提供服务");
                return new SipRoutingInfo {
                    ProxyServer = selected.ProxyServer,
                    OutboundProxy = selected.OutboundProxy,
                    SelectedLine = selected,
                    IsUsingDefaultSipServer = false
                };
            }

            _logger.LogWarning($"首选线路 {preferredLineId.Value} 对于账户 {sipAccount.Id} 不存在或未激活，后续逻辑将决定回退策略");
        }

        if (!autoSelectLine && !preferredLineId.HasValue) {
            if (!string.IsNullOrEmpty(sipAccount.SipServer)) {
                _logger.LogDebug($"未启用自动选择且未指定首选线路，使用账户 {sipAccount.Id} 的 SipServer {sipAccount.SipServer}");
                return new SipRoutingInfo {
                    ProxyServer = sipAccount.SipServer,
                    OutboundProxy = null,
                    SelectedLine = null,
                    IsUsingDefaultSipServer = true
                };
            }

            _logger.LogWarning($"未启用自动选择且账户 {sipAccount.Id} 未配置 SipServer，无法选择路由");
            throw new InvalidOperationException("未找到可用的SIP线路");
        }

        if (autoSelectLine) {
            if (sipAccount.DefaultLineId.HasValue) {
                selected = await _db.SipLines.FirstOrDefaultAsync(l => l.Id == sipAccount.DefaultLineId && l.IsActive);
                if (selected != null) {
                    _logger.LogDebug($"使用账户默认线路 {selected.Name} ({selected.Id}) 为账户 {sipAccount.Id} 提供服务");
                    return new SipRoutingInfo {
                        ProxyServer = selected.ProxyServer,
                        OutboundProxy = selected.OutboundProxy,
                        SelectedLine = selected,
                        IsUsingDefaultSipServer = false
                    };
                }
            }

            selected = sipAccount.AvailableLines.Where(l => l.IsActive).OrderByDescending(l => l.Priority).FirstOrDefault();
            if (selected != null) {
                _logger.LogDebug($"按优先级自动选择线路 {selected.Name} ({selected.Id}) 为账户 {sipAccount.Id} 提供服务");
                return new SipRoutingInfo {
                    ProxyServer = selected.ProxyServer,
                    OutboundProxy = selected.OutboundProxy,
                    SelectedLine = selected,
                    IsUsingDefaultSipServer = false
                };
            }

            selected = await _db.SipLines.Where(l => l.IsActive).OrderByDescending(l => l.Priority).FirstOrDefaultAsync();
            if (selected != null) {
                _logger.LogDebug($"自动选择后退回至全局线路 {selected.Name} ({selected.Id})");
                return new SipRoutingInfo {
                    ProxyServer = selected.ProxyServer,
                    OutboundProxy = selected.OutboundProxy,
                    SelectedLine = selected,
                    IsUsingDefaultSipServer = false
                };
            }
        }

        if (!string.IsNullOrEmpty(sipAccount.SipServer)) {
            _logger.LogWarning($"没有可用的SIP线路，回退使用账户 {sipAccount.Id} 的 SipServer {sipAccount.SipServer}");
            return new SipRoutingInfo {
                ProxyServer = sipAccount.SipServer,
                OutboundProxy = null,
                SelectedLine = null,
                IsUsingDefaultSipServer = true
            };
        }

        _logger.LogWarning("没有可用的SIP线路来路由呼叫，且账户未配置SipServer");
        throw new InvalidOperationException("未找到可用的SIP线路");
    }
}