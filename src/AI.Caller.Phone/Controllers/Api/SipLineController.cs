using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SipLineController : ControllerBase {
    private readonly AppDbContext _context;
    private readonly ILogger<SipLineController> _logger;

    public SipLineController(AppDbContext context, ILogger<SipLineController> logger) {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有SIP线路
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SipLine>>> GetLines() {
        var lines = await _context.SipLines
            .OrderByDescending(l => l.Priority)
            .ThenBy(l => l.Name)
            .ToListAsync();
        
        return Ok(lines);
    }

    /// <summary>
    /// 根据ID获取SIP线路
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<SipLine>> GetLine(int id) {
        var line = await _context.SipLines.FindAsync(id);
        
        if (line == null) {
            return NotFound();
        }
        
        return Ok(line);
    }

    /// <summary>
    /// 创建新的SIP线路
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SipLine>> CreateLine(CreateSipLineRequest request) {
        if (!ModelState.IsValid) {
            return BadRequest(ModelState);
        }

        var line = new SipLine {
            Name = request.Name,
            ProxyServer = request.ProxyServer,
            OutboundProxy = request.OutboundProxy,
            IsActive = request.IsActive,
            Priority = request.Priority,
            Region = request.Region,
            Description = request.Description
        };

        _context.SipLines.Add(line);
        await _context.SaveChangesAsync();

        _logger.LogInformation("用户 {User} 创建了SIP线路 {LineName} (ID: {LineId})", User.Identity?.Name, line.Name, line.Id);

        return CreatedAtAction(nameof(GetLine), new { id = line.Id }, line);
    }

    /// <summary>
    /// 更新SIP线路
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateLine(int id, UpdateSipLineRequest request) {
        if (!ModelState.IsValid) {
            return BadRequest(ModelState);
        }

        var line = await _context.SipLines.FindAsync(id);
        if (line == null) {
            return NotFound();
        }

        line.Name = request.Name;
        line.ProxyServer = request.ProxyServer;
        line.OutboundProxy = request.OutboundProxy;
        line.IsActive = request.IsActive;
        line.Priority = request.Priority;
        line.Region = request.Region;
        line.Description = request.Description;

        await _context.SaveChangesAsync();

        _logger.LogInformation("用户 {User} 更新了SIP线路 {LineName} (ID: {LineId})", User.Identity?.Name, line.Name, line.Id);

        return NoContent();
    }

    /// <summary>
    /// 删除SIP线路
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteLine(int id) {
        var line = await _context.SipLines.FindAsync(id);
        if (line == null) {
            return NotFound();
        }

        // 检查是否有SIP账户正在使用此线路
        var accountsUsingLine = await _context.SipAccounts
            .Where(a => a.DefaultLineId == id)
            .CountAsync();

        if (accountsUsingLine > 0) {
            return BadRequest(new { 
                message = $"无法删除线路，有 {accountsUsingLine} 个SIP账户正在使用此线路作为默认线路" 
            });
        }

        _context.SipLines.Remove(line);
        await _context.SaveChangesAsync();

        _logger.LogInformation("用户 {User} 删除了SIP线路 {LineName} (ID: {LineId})", User.Identity?.Name, line.Name, line.Id);

        return NoContent();
    }

    /// <summary>
    /// 设置SIP账户的默认线路
    /// </summary>
    [HttpPost("set-default")]
    public async Task<IActionResult> SetDefaultLine(SetDefaultLineRequest request) {
        if (!ModelState.IsValid) {
            return BadRequest(ModelState);
        }

        var account = await _context.SipAccounts
            .Include(a => a.DefaultLine)
            .FirstOrDefaultAsync(a => a.Id == request.SipAccountId);
        
        if (account == null) {
            return NotFound("SIP账户不存在");
        }

        var line = await _context.SipLines.FindAsync(request.LineId);
        if (line == null) {
            return NotFound("SIP线路不存在");
        }

        account.DefaultLineId = request.LineId;
        await _context.SaveChangesAsync();

        _logger.LogInformation("用户 {User} 设置SIP账户 {SipAccount} 的默认线路为 {LineName} (ID: {LineId})", User.Identity?.Name, account.SipUsername, line.Name, line.Id);

        return NoContent();
    }

    /// <summary>
    /// 获取SIP账户的可用线路
    /// </summary>
    [HttpGet("account/{accountId}/lines")]
    public async Task<ActionResult<List<SipLine>>> GetAccountLines(int accountId) {
        var account = await _context.SipAccounts
            .Include(a => a.AvailableLines)
            .Include(a => a.DefaultLine)
            .FirstOrDefaultAsync(a => a.Id == accountId);
        
        if (account == null) {
            return NotFound("SIP账户不存在");
        }

        // 获取所有可用线路
        var allLines = await _context.SipLines
            .OrderByDescending(l => l.Priority)
            .ThenBy(l => l.Name)
            .ToListAsync();

        // 标记哪些线路已关联到账户
        var result = allLines.Select(line => new {
            line.Id,
            line.Name,
            line.ProxyServer,
            line.OutboundProxy,
            line.IsActive,
            line.Priority,
            line.Region,
            line.Description,
            line.CreatedAt,
            IsAssociated = account.AvailableLines.Any(l => l.Id == line.Id),
            IsDefault = account.DefaultLineId == line.Id
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// 关联SIP账户和线路
    /// </summary>
    [HttpPost("account/{accountId}/lines/{lineId}")]
    public async Task<IActionResult> AssociateLine(int accountId, int lineId) {
        var account = await _context.SipAccounts
            .Include(a => a.AvailableLines)
            .FirstOrDefaultAsync(a => a.Id == accountId);
        
        if (account == null) {
            return NotFound("SIP账户不存在");
        }

        var line = await _context.SipLines.FindAsync(lineId);
        if (line == null) {
            return NotFound("SIP线路不存在");
        }

        if (account.AvailableLines.Any(l => l.Id == lineId)) {
            return BadRequest("线路已关联到此账户");
        }

        account.AvailableLines.Add(line);
        await _context.SaveChangesAsync();

        _logger.LogInformation("用户 {User} 关联SIP账户 {SipAccount} 与线路 {LineName} (ID: {LineId})", User.Identity?.Name, account.SipUsername, line.Name, line.Id);

        return NoContent();
    }

    /// <summary>
    /// 取消关联SIP账户和线路
    /// </summary>
    [HttpDelete("account/{accountId}/lines/{lineId}")]
    public async Task<IActionResult> DisassociateLine(int accountId, int lineId) {
        var account = await _context.SipAccounts
            .Include(a => a.AvailableLines)
            .FirstOrDefaultAsync(a => a.Id == accountId);
        
        if (account == null) {
            return NotFound("SIP账户不存在");
        }

        var line = account.AvailableLines.FirstOrDefault(l => l.Id == lineId);
        if (line == null) {
            return NotFound("线路未关联到此账户");
        }

        // 如果是默认线路，需要先取消默认设置
        if (account.DefaultLineId == lineId) {
            account.DefaultLineId = null;
        }

        account.AvailableLines.Remove(line);
        await _context.SaveChangesAsync();

        _logger.LogInformation("用户 {User} 取消关联SIP账户 {SipAccount} 与线路 {LineName} (ID: {LineId})", User.Identity?.Name, account.SipUsername, line.Name, line.Id);

        return NoContent();
    }

    /// <summary>
    /// 获取当前登录用户的SIP账户可用线路（基于用户的SipAccount）
    /// </summary>
    [HttpGet("account/lines")]
    public async Task<ActionResult<List<SipLine>>> GetCurrentAccountLines() {
        var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);

        var user = await _context.Users
            .Include(u => u.SipAccount!)
            .ThenInclude(a => a.AvailableLines)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null || user.SipAccount == null) {
            return NotFound("当前用户未绑定SIP账户");
        }

        return await GetAccountLines(user.SipAccount.Id);
    }
}

// 请求模型
public class CreateSipLineRequest {
    public string Name { get; set; } = string.Empty;
    public string ProxyServer { get; set; } = string.Empty;
    public string? OutboundProxy { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 0;
    public string? Region { get; set; }
    public string? Description { get; set; }
}

public class UpdateSipLineRequest {
    public string Name { get; set; } = string.Empty;
    public string ProxyServer { get; set; } = string.Empty;
    public string? OutboundProxy { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 0;
    public string? Region { get; set; }
    public string? Description { get; set; }
}

public class SetDefaultLineRequest {
    public int SipAccountId { get; set; }
    public int LineId { get; set; }
}