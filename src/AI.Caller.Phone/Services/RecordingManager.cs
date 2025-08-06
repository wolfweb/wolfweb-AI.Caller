using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Services
{
    /// <summary>
    /// 录音管理器 - 统一管理录音功能，与SipService集成但不侵入
    /// </summary>
    public class RecordingManager
    {
        private readonly ISimpleRecordingService _recordingService;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ApplicationContext _applicationContext;
        private readonly ILogger<RecordingManager> _logger;

        public RecordingManager(
            ISimpleRecordingService recordingService,
            IServiceScopeFactory serviceScopeFactory,
            ApplicationContext applicationContext,            
            ILogger<RecordingManager> logger)
        {
            _recordingService = recordingService;
            _applicationContext = applicationContext;
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// 初始化录音管理器 - 订阅SIP事件
        /// </summary>
        public void Initialize()
        {
            // 监听新的SIP客户端连接
            _applicationContext.OnSipClientAdded += OnSipClientAdded;
            _applicationContext.OnSipClientRemoved += OnSipClientRemoved;
        }

        /// <summary>
        /// 当新的SIP客户端添加时
        /// </summary>
        private void OnSipClientAdded(string sipUsername, SIPClient sipClient)
        {
            try
            {
                // 订阅通话相关事件
                sipClient.CallAnswered += async (client) => await OnCallAnswered(sipUsername, client);
                sipClient.CallEnded += async (client) => await OnCallEnded(sipUsername, client);
                
                _logger.LogDebug($"已为SIP客户端 {sipUsername} 订阅录音事件");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"为SIP客户端 {sipUsername} 订阅录音事件失败");
            }
        }

        /// <summary>
        /// 当SIP客户端移除时
        /// </summary>
        private void OnSipClientRemoved(string sipUsername, SIPClient sipClient)
        {
            try
            {
                _ = Task.Run(async () =>
                {
                    await _recordingService.StopRecordingAsync(sipUsername);
                });
                
                _logger.LogDebug($"已清理SIP客户端 {sipUsername} 的录音资源");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"清理SIP客户端 {sipUsername} 录音资源失败");
            }
        }

        /// <summary>
        /// 通话接听时的处理
        /// </summary>
        private async Task OnCallAnswered(string sipUsername, SIPClient sipClient)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == sipUsername);
                if (user != null && user.AutoRecording)
                {
                    // 延迟一点时间确保通话稳定
                    await Task.Delay(1000);
                    
                    var result = await _recordingService.StartRecordingAsync(sipUsername);
                    if (result)
                    {
                        _logger.LogInformation($"自动录音已开始 - 用户: {sipUsername}");
                    }
                    else
                    {
                        _logger.LogWarning($"自动录音开始失败 - 用户: {sipUsername}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理通话接听录音失败 - 用户: {sipUsername}");
            }
        }

        /// <summary>
        /// 通话结束时的处理
        /// </summary>
        private async Task OnCallEnded(string sipUsername, SIPClient sipClient)
        {
            try
            {
                // 自动停止录音
                var result = await _recordingService.StopRecordingAsync(sipUsername);
                if (result)
                {
                    _logger.LogInformation($"通话结束，录音已自动停止 - 用户: {sipUsername}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理通话结束录音失败 - 用户: {sipUsername}");
            }
        }

        /// <summary>
        /// 手动开始录音
        /// </summary>
        public async Task<bool> StartRecordingAsync(string sipUsername)
        {
            return await _recordingService.StartRecordingAsync(sipUsername);
        }

        /// <summary>
        /// 手动停止录音
        /// </summary>
        public async Task<bool> StopRecordingAsync(string sipUsername)
        {
            return await _recordingService.StopRecordingAsync(sipUsername);
        }

        /// <summary>
        /// 获取录音状态
        /// </summary>
        public async Task<Models.RecordingStatus?> GetRecordingStatusAsync(string sipUsername)
        {
            return await _recordingService.GetRecordingStatusAsync(sipUsername);
        }
    }
}