using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Services;

public class CallFlowOrchestrator : ICallFlowOrchestrator {
    private readonly ILogger<CallFlowOrchestrator> _logger;
    private readonly ITtsPlayerService _ttsPlayer;
    private readonly ICallManager _callManager;
    private readonly IServiceScopeFactory _scopeFactory;

    public CallFlowOrchestrator(
        ILogger<CallFlowOrchestrator> logger,
        ITtsPlayerService ttsPlayer,
        ICallManager callManager,
        IServiceScopeFactory scopeFactory) {
        _logger = logger;
        _ttsPlayer = ttsPlayer;
        _callManager = callManager;
        _scopeFactory = scopeFactory;
    }

    public async Task HandleInboundCallAsync(CallContext callContext) {
        await Task.Delay(1000);
        using var scope = _scopeFactory.CreateScope();
        var settingProvider = scope.ServiceProvider.GetRequiredService<IAICustomerServiceSettingsProvider>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await settingProvider.GetSettingsAsync();

        var template = await dbContext.TtsTemplates.FirstOrDefaultAsync(t => t.Id == settings.DefaultTtsTemplateId);

        if (template == null) {
            _logger.LogWarning("No active TTS template found for call {CallId}. Hanging up.", callContext.CallId);
            return;
        }

        if (callContext.Callee == null || callContext.Callee.User == null || callContext.Callee.Client == null) throw new Exception($"呼叫上下文被叫未初始化");

        _logger.LogInformation("Found TTS template '{TemplateName}' for call {CallId}", template.Name, callContext.CallId);
        
        var ttsGenerationTime = await _ttsPlayer.PlayTtsAsync(template.Content, callContext.Callee.User, callContext.Callee.Client.Client, template.SpeechRate);
        
        if (template.PlayCount > 1) {
            for(var i = 0; i < template.PlayCount - 1; i++) {
                var desiredPause = TimeSpan.FromSeconds(template.PauseBetweenPlaysInSeconds);
                var actualWaitTime = desiredPause - ttsGenerationTime;
                
                if (actualWaitTime > TimeSpan.Zero) {
                    _logger.LogInformation("Waiting {WaitTime}ms before next play (desired: {DesiredPause}ms, TTS generation: {TtsTime}ms)", 
                        actualWaitTime.TotalMilliseconds, desiredPause.TotalMilliseconds, ttsGenerationTime.TotalMilliseconds);
                    await Task.Delay(actualWaitTime);
                } else {
                    _logger.LogInformation("No wait needed, TTS generation time ({TtsTime}ms) exceeded desired pause ({DesiredPause}ms)", 
                        ttsGenerationTime.TotalMilliseconds, desiredPause.TotalMilliseconds);
                }
                
                ttsGenerationTime = await _ttsPlayer.PlayTtsAsync(template.Content, callContext.Callee.User, callContext.Callee.Client.Client, template.SpeechRate);
            }
        }
        
        if(!string.IsNullOrEmpty(template.EndingSpeech)) {
            var desiredPause = TimeSpan.FromSeconds(template.PauseBetweenPlaysInSeconds);
            var actualWaitTime = desiredPause - ttsGenerationTime;
            
            if (actualWaitTime > TimeSpan.Zero) {
                _logger.LogInformation("Waiting {WaitTime}ms before ending speech (desired: {DesiredPause}ms, TTS generation: {TtsTime}ms)", 
                    actualWaitTime.TotalMilliseconds, desiredPause.TotalMilliseconds, ttsGenerationTime.TotalMilliseconds);
                await Task.Delay(actualWaitTime);
            } else {
                _logger.LogInformation("No wait needed for ending speech, TTS generation time ({TtsTime}ms) exceeded desired pause ({DesiredPause}ms)", 
                    ttsGenerationTime.TotalMilliseconds, desiredPause.TotalMilliseconds);
            }
            
            await _ttsPlayer.PlayTtsAsync(template.EndingSpeech, callContext.Callee.User, callContext.Callee.Client.Client, template.SpeechRate);
        }

        _logger.LogInformation("Finished playing initial TTS for call {CallId}", callContext.CallId);
        
        _ttsPlayer.StopPlayout(callContext.Callee.User);
        
        await _callManager.HangupCallAsync(callContext.CallId, callContext.Caller!.User!.Id);
    }
}