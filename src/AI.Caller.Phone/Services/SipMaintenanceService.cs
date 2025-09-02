using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Phone.Services;

public class SipMaintenanceService : BackgroundService {
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SipMaintenanceService> _logger;
    private readonly TimeSpan _maintenanceInterval = TimeSpan.FromMinutes(10);

    public SipMaintenanceService(IServiceProvider serviceProvider, ILogger<SipMaintenanceService> logger) {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _logger.LogInformation("SIP维护服务已启动");

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await PerformMaintenanceAsync();
                await Task.Delay(_maintenanceInterval, stoppingToken);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                _logger.LogError(ex, "SIP维护服务执行时发生错误");

                try {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                } catch (OperationCanceledException) {
                    break;
                }
            }
        }

        _logger.LogInformation("SIP维护服务已停止");
    }

    private async Task PerformMaintenanceAsync() {
        using var scope = _serviceProvider.CreateScope();
        var sipService = scope.ServiceProvider.GetRequiredService<SipService>();

        try {
            _logger.LogDebug("开始执行SIP维护任务");
            await sipService.PerformMaintenanceAsync();
            _logger.LogDebug("SIP维护任务完成");
        } catch (Exception ex) {
            _logger.LogError(ex, "执行SIP维护任务时发生错误");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("正在停止SIP维护服务...");
        await base.StopAsync(cancellationToken);
    }
}