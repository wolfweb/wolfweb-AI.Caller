using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Services;

public class CallFlowOrchestrator : ICallFlowOrchestrator {
    private readonly ILogger<CallFlowOrchestrator> _logger;
    private readonly ITtsPlayerService _ttsPlayer;
    private readonly IServiceScopeFactory _scopeFactory;

    public CallFlowOrchestrator(
        ILogger<CallFlowOrchestrator> logger,
        ITtsPlayerService ttsPlayer,
        IServiceScopeFactory scopeFactory) {
        _logger = logger;
        _ttsPlayer = ttsPlayer;
        _scopeFactory = scopeFactory;
    }

    public async Task HandleInboundCallAsync(CallContext callContext) {
        await Task.Delay(1000);
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var template = await dbContext.TtsTemplates.FirstOrDefaultAsync(t => t.IsActive);

        if (template == null) {
            _logger.LogWarning("No active TTS template found for call {CallId}. Hanging up.", callContext.CallId);
            return;
        }

        if (callContext.Callee == null || callContext.Callee.User == null || callContext.Callee.Client == null) throw new Exception($"呼叫上下文被叫未初始化");

        _logger.LogInformation("Found TTS template '{TemplateName}' for call {CallId}", template.Name, callContext.CallId);
        await _ttsPlayer.PlayTtsAsync(template.Content, callContext.Callee.User,  callContext.Callee.Client.Client, template.SpeechRate);
        if (template.PlayCount > 1) {
            for(var i=0;i<template.PlayCount - 1; i++) {
                await Task.Delay(template.PauseBetweenPlaysInSeconds);
                await _ttsPlayer.PlayTtsAsync(template.Content, callContext.Callee.User, callContext.Callee.Client.Client, template.SpeechRate);
            }
        }
        if(!string.IsNullOrEmpty(template.EndingSpeech)) {
            await Task.Delay(template.PauseBetweenPlaysInSeconds);
            await _ttsPlayer.PlayTtsAsync(template.EndingSpeech, callContext.Callee.User, callContext.Callee.Client.Client, template.SpeechRate);
        }

        _logger.LogInformation("Finished playing initial TTS for call {CallId}", callContext.CallId);
    }
}