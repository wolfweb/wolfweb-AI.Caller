using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AI.Caller.Phone.Controllers {
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class AICustomerServiceController : ControllerBase {
        private readonly SipService _sipService;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<AICustomerServiceController> _logger;

        public AICustomerServiceController(
            SipService sipService,
            AppDbContext dbContext,
            ILogger<AICustomerServiceController> logger) {
            _sipService = sipService;
            _dbContext = dbContext;
            _logger = logger;
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartAICustomerService([FromBody] StartAICustomerServiceRequest request) {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var user = await _dbContext.Users
                    .Include(u => u.SipAccount)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null) {
                    return BadRequest(new { message = "用户不存在" });
                }

                if (string.IsNullOrWhiteSpace(request.ScriptText)) {
                    return BadRequest(new { message = "脚本内容不能为空" });
                }

                var result = await _sipService.StartAICustomerServiceAsync(user, request.ScriptText);
                
                if (result) {
                    return Ok(new { 
                        message = "AI客服启动成功",
                        scriptText = request.ScriptText,
                        timestamp = DateTime.UtcNow
                    });
                } else {
                    return BadRequest(new { message = "AI客服启动失败，请检查是否有活跃通话" });
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "启动AI客服时发生错误");
                return StatusCode(500, new { message = "服务器内部错误" });
            }
        }

        [HttpPost("stop")]
        public async Task<IActionResult> StopAICustomerService() {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null) {
                    return BadRequest(new { message = "用户不存在" });
                }

                var result = await _sipService.StopAICustomerServiceAsync(user);
                
                if (result) {
                    return Ok(new { 
                        message = "AI客服停止成功",
                        timestamp = DateTime.UtcNow
                    });
                } else {
                    return BadRequest(new { message = "AI客服停止失败或未在运行" });
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "停止AI客服时发生错误");
                return StatusCode(500, new { message = "服务器内部错误" });
            }
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetAICustomerServiceStatus() {
            try {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var isActive = _sipService.IsAICustomerServiceActive(userId);
                
                return Ok(new { 
                    isActive = isActive,
                    timestamp = DateTime.UtcNow
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "获取AI客服状态时发生错误");
                return StatusCode(500, new { message = "服务器内部错误" });
            }
        }

        [HttpGet("scripts")]
        public IActionResult GetPresetScripts() {
            try {
                var scripts = new[] {
                    new { 
                        id = 1, 
                        name = "欢迎致电", 
                        content = "您好，欢迎致电我们公司，我是AI客服小助手。请问有什么可以帮助您的吗？如需人工服务，请按0转接。" 
                    },
                    new { 
                        id = 2, 
                        name = "业务咨询", 
                        content = "感谢您对我们产品的关注。我们提供专业的解决方案，详细信息请访问我们的官网或联系人工客服。" 
                    },
                    new { 
                        id = 3, 
                        name = "售后服务", 
                        content = "您好，这里是售后服务部门。请详细描述您遇到的问题，我们会尽快为您处理。" 
                    },
                    new { 
                        id = 4, 
                        name = "暂时离开", 
                        content = "抱歉，客服人员暂时离开，请稍后再试或留下您的联系方式，我们会尽快回复您。" 
                    }
                };

                return Ok(new { scripts = scripts });
            } catch (Exception ex) {
                _logger.LogError(ex, "获取预设脚本时发生错误");
                return StatusCode(500, new { message = "服务器内部错误" });
            }
        }
    }

    public class StartAICustomerServiceRequest {
        public string ScriptText { get; set; } = string.Empty;        
        public int SpeakerId { get; set; } = 0;        
        public float Speed { get; set; } = 1.0f;
    }
}