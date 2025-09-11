using AI.Caller.Core;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Services {
    public class RecordingManager {
        private readonly ISimpleRecordingService _recordingService;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ApplicationContext _applicationContext;
        private readonly ILogger<RecordingManager> _logger;

        public RecordingManager(
            ISimpleRecordingService recordingService,
            IServiceScopeFactory serviceScopeFactory,
            ApplicationContext applicationContext,
            ILogger<RecordingManager> logger) {
            _recordingService = recordingService;
            _applicationContext = applicationContext;
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        public void Initialize() {
            _applicationContext.OnSipClientAdded += OnSipClientAdded;
            _applicationContext.OnSipClientRemoved += OnSipClientRemoved;
        }

        private void OnSipClientAdded(int userId, SIPClient sipClient) {
            try {
                sipClient.CallAnswered += async (client) => await OnCallAnswered(userId, client);
                sipClient.CallEnded += async (client) => await OnCallEnded(userId, client);

                _logger.LogDebug($"已为SIP客户端 {userId} 订阅录音事件");
            } catch (Exception ex) {
                _logger.LogError(ex, $"为SIP客户端 {userId} 订阅录音事件失败");
            }
        }

        private void OnSipClientRemoved(int userId, SIPClient sipClient) {
            try {
                _ = Task.Run(async () => {
                    await _recordingService.StopRecordingAsync(userId);
                });

                _logger.LogDebug($"已清理SIP客户端 {userId} 的录音资源");
            } catch (Exception ex) {
                _logger.LogError(ex, $"清理SIP客户端 {userId} 录音资源失败");
            }
        }

        private async Task OnCallAnswered(int userId, SIPClient sipClient) {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                var _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await _dbContext.Users
                    .Include(u => u.SipAccount)
                    .FirstOrDefaultAsync(u => u.Id == userId);
                
                if (user != null) {
                    // 延迟一点时间确保通话稳定
                    await Task.Delay(1000);

                    var result = await _recordingService.StartRecordingAsync(userId);
                    if (result) {
                        _logger.LogInformation($"自动录音已开始 - 用户: {userId} (全局自动录音)");
                    } else {
                        _logger.LogWarning($"自动录音开始失败 - 用户: {userId}");
                    }
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理通话接听录音失败 - 用户: {userId}");
            }
        }

        private async Task OnCallEnded(int userId, SIPClient sipClient) {
            try {
                var result = await _recordingService.StopRecordingAsync(userId);
                if (result) {
                    _logger.LogInformation($"通话结束，录音已自动停止 - 用户: {userId}");
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理通话结束录音失败 - 用户: {userId}");
            }
        }
    }
}