using AI.Caller.Core;
using AI.Caller.Core.Models;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AI.Caller.Phone.Services;

public class CallProcessor : ICallProcessor {
    private readonly ILogger<CallProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;

    public CallProcessor(ILogger<CallProcessor> logger, IServiceProvider serviceProvider) {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task ProcessCallLogJob(int callLogId) {
        _logger.LogInformation("Starting to process CallLogId {CallLogId}.", callLogId);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var callManager = scope.ServiceProvider.GetRequiredService<ICallManager>();
        var ttsPlayer = scope.ServiceProvider.GetRequiredService<ITtsPlayerService>();

        var callLog = await context.CallLogs
            .Include(l => l.BatchCallJob)
            .FirstOrDefaultAsync(l => l.Id == callLogId);

        if (callLog == null) {
            _logger.LogWarning("CallLog with ID {CallLogId} not found for processing.", callLogId);
            return;
        }
        
        if (callLog.BatchCallJob != null && (callLog.BatchCallJob.Status == BatchJobStatus.Paused || callLog.BatchCallJob.Status == BatchJobStatus.Cancelled)) {
            _logger.LogInformation("Skipping CallLogId {callLogId} because parent BatchJobId {batchJobId} is {status}.", callLog.Id, callLog.BatchCallJob.Id, callLog.BatchCallJob.Status);
            return;
        }

        User? agent = null;
        try {
            var activeCallUserIds = callManager.GetActiviteUsers().Select(u => u.Id).ToHashSet();
            agent = await context.Users
                .Include(u => u.SipAccount)
                .Where(u => u.SipAccount != null && u.SipAccount.IsActive)
                .FirstOrDefaultAsync(u => !activeCallUserIds.Contains(u.Id));
        } catch (Exception ex) {
            _logger.LogError(ex, "Error finding an idle agent for CallLogId {CallLogId}.", callLogId);
            callLog.Status = Entities.CallStatus.Failed;
            callLog.FailureReason = "System error while finding an idle agent.";
            callLog.CompletedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return;
        }

        if (agent == null) {
            _logger.LogWarning("No idle AI agents available for CallLogId {CallLogId}. Task will be retried.", callLogId);
            return;
        }

        _logger.LogInformation("Found idle agent '{AgentUsername}' (ID: {AgentId}) for CallLogId {CallLogId}.", agent.Username, agent.Id, callLogId);

        callLog.Status = Entities.CallStatus.InProgress;
        await context.SaveChangesAsync();

        var tcs = new TaskCompletionSource<CallResult>();
        bool callWasAnswered = false;
        SIPClient? sipClient = null;
        CallContext? callContext = null;

        try {
            Action<SIPClient> callAnsweredHandler = null!;
            Action<SIPClient, HangupEventContext> callFinishedHandler = null!;

            var webUser = await context.Users.Include(x => x.SipAccount).FirstOrDefaultAsync(x => x.SipAccount != null && x.SipAccount.SipUsername == callLog.CalleeNumber);
            
            int? selectedLineId = null;
            bool autoSelectLine = true;
            
            if (callLog.BatchCallJob != null) {
                selectedLineId = callLog.BatchCallJob.SelectedLineId;
                autoSelectLine = callLog.BatchCallJob.AutoSelectLine;
            }
            
            callContext = await callManager.MakeCallAsync($"sip:{callLog.CalleeNumber}", agent, null, webUser != null ? CallScenario.ServerToWeb : CallScenario.ServerToMobile, selectedLineId, autoSelectLine);

            callAnsweredHandler = async (sc) => {
                _logger.LogInformation("Call answered for CallLogId {CallLogId}. Starting AI Customer Service.", callLogId);
                callWasAnswered = true;
                
                try {
                    var batchCall = await context.BatchCallJobs.FindAsync(callLog.BatchCallJobId);
                    
                    if (batchCall != null && batchCall.ScenarioRecordingId.HasValue) {
                        _logger.LogInformation("Executing Scenario Recording {ScenarioId} for CallLogId {CallLogId}", batchCall.ScenarioRecordingId, callLogId);
                        
                        var aiManager = scope.ServiceProvider.GetRequiredService<AICustomerServiceManager>();
                        var scenarioService = scope.ServiceProvider.GetRequiredService<IScenarioRecordingService>();
                        
                        var scenario = await scenarioService.GetScenarioRecordingAsync(batchCall.ScenarioRecordingId.Value);
                        if (scenario == null) {
                            throw new InvalidOperationException($"Scenario Recording with ID {batchCall.ScenarioRecordingId} not found.");
                        }

                        var variables = JsonSerializer.Deserialize<Dictionary<string, string>>(callLog.ResolvedContent) ?? new Dictionary<string, string>();

                        if(await aiManager.StartScenarioServiceAsync(agent, sc, scenario, variables, callContext.CallId)) {
                            await aiManager.GetSessionByCallId(callContext.CallId)!.PlaybackTask!;
                        }
                        await aiManager.StopAICustomerServiceAsync(agent.Id);
                        await callManager.HangupCallAsync(callContext.CallId, callContext.Caller!.User!.Id);
                    } else {
                        // Legacy TTS Template Mode
                        var ttsTemplate = await context.TtsTemplates.FindAsync(batchCall!.TtsTemplateId);

                        var ttsGenerationTime = await ttsPlayer.PlayTtsAsync(callLog.ResolvedContent, agent, sc, ttsTemplate?.SpeechRate);
                        
                        if (ttsTemplate?.PlayCount > 1) {
                            for (var i = 0; i < ttsTemplate.PlayCount - 1; i++) {
                                var desiredPause = TimeSpan.FromSeconds(ttsTemplate.PauseBetweenPlaysInSeconds);
                                var actualWaitTime = desiredPause - ttsGenerationTime;
                                
                                if (actualWaitTime > TimeSpan.Zero) {
                                    _logger.LogInformation("Waiting {WaitTime}ms before next play for CallLogId {CallLogId}", actualWaitTime.TotalMilliseconds, callLogId);
                                    await Task.Delay(actualWaitTime);
                                }
                                
                                ttsGenerationTime = await ttsPlayer.PlayTtsAsync(callLog.ResolvedContent, agent, sc, ttsTemplate.SpeechRate);
                            }
                        }

                        if (!string.IsNullOrEmpty(ttsTemplate?.EndingSpeech)) {
                            var desiredPause = TimeSpan.FromSeconds(ttsTemplate.PauseBetweenPlaysInSeconds);
                            var actualWaitTime = desiredPause - ttsGenerationTime;
                            
                            if (actualWaitTime > TimeSpan.Zero) {
                                _logger.LogInformation("Waiting {WaitTime}ms before ending speech for CallLogId {CallLogId}", actualWaitTime.TotalMilliseconds, callLogId);
                                await Task.Delay(actualWaitTime);
                            }
                            
                            await ttsPlayer.PlayTtsAsync(ttsTemplate.EndingSpeech, agent, sc, ttsTemplate.SpeechRate);
                        }

                        await ttsPlayer.StopPlayoutAsync(agent);
                        await callManager.HangupCallAsync(callContext.CallId, callContext.Caller!.User!.Id);
                    }

                    tcs.TrySetResult(new CallResult { Status = CallOutcome.Completed });
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error starting AI Customer Service after call answered for CallLogId {CallLogId}.", callLogId);
                    tcs.TrySetResult(new CallResult { Status = CallOutcome.Failed, FailureReason = ex.Message });
                    await callManager.HangupCallAsync(callContext.CallId, callContext.Caller!.User!.Id);
                }
            };

            callFinishedHandler = (sc, hangupContext) => {
                sc.CallAnswered -= callAnsweredHandler;
                sc.CallFinishedWithContext -= callFinishedHandler;

                if (callWasAnswered) {
                    tcs.TrySetResult(new CallResult { Status = CallOutcome.Completed });
                } else {
                    tcs.TrySetResult(new CallResult { Status = CallOutcome.NoAnswer, FailureReason = "Call was not answered." });
                }
            };

            if (callContext.Caller?.Client?.Client == null) {
                throw new InvalidOperationException("Failed to create a valid call context or SIP client from CallManager.");
            }
            sipClient = callContext.Caller.Client.Client;

            sipClient.CallAnswered += callAnsweredHandler;
            sipClient.CallFinishedWithContext += callFinishedHandler;

            _logger.LogInformation("Call initiated for CallLogId {CallLogId}. Waiting for answer to start AI service.", callLogId);

            var callResult = await tcs.Task;
            _logger.LogInformation("Waiting for call completion for CallLogId {CallLogId}.", callLogId);

            if (callResult.Status == CallOutcome.Completed) {
                callLog.Status = Entities.CallStatus.Completed;
                _logger.LogInformation("Call to {PhoneNumber} completed successfully for CallLogId {CallLogId}.", callLog.CalleeNumber, callLogId);
            } else if (callResult.Status == CallOutcome.NoAnswer) {
                callLog.Status = Entities.CallStatus.NoAnswer;
                callLog.FailureReason = callResult.FailureReason ?? "No answer";
                _logger.LogWarning("Call to {PhoneNumber} was not answered for CallLogId {CallLogId}.", callLog.CalleeNumber, callLogId);
            } else {
                callLog.Status = Entities.CallStatus.Failed;
                callLog.FailureReason = callResult.FailureReason ?? "Unknown failure";
                _logger.LogWarning("Call to {PhoneNumber} failed for CallLogId {CallLogId}: {FailureReason}", callLog.CalleeNumber, callLogId, callLog.FailureReason);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "An error occurred while processing CallLogId {CallLogId}.", callLogId);
            callLog.Status = Entities.CallStatus.Failed;
            callLog.FailureReason = $"An unexpected error occurred: {ex.Message}";
            if (callContext != null) {
                await callManager.HangupCallAsync(callContext.CallId, callContext.Caller!.User!.Id);
            }
        } finally {
            callLog.CompletedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();

            if (callLog.BatchCallJobId.HasValue) {
                await UpdateBatchJobProgress(callLog.BatchCallJobId.Value);
            }
        }
    }

    private async Task UpdateBatchJobProgress(int batchJobId) {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var batchJob = await context.BatchCallJobs.Include(j => j.CallLogs).FirstOrDefaultAsync(j => j.Id == batchJobId);
        if (batchJob == null) return;

        var stats = batchJob.CallLogs
            .GroupBy(l => 1)
            .Select(g => new {
                Processed = g.Count(l => l.Status == Entities.CallStatus.Completed || l.Status == Entities.CallStatus.Failed || l.Status == Entities.CallStatus.NoAnswer),
                Succeeded = g.Count(l => l.Status == Entities.CallStatus.Completed),
                Failed = g.Count(l => l.Status == Entities.CallStatus.Failed || l.Status == Entities.CallStatus.NoAnswer)
            })
            .FirstOrDefault();

        if (stats != null) {
            batchJob.ProcessedCount = stats.Processed;
            batchJob.SuccessCount = stats.Succeeded;
            batchJob.FailedCount = stats.Failed;

            if (batchJob.ProcessedCount >= batchJob.TotalCount) {
                batchJob.Status = batchJob.FailedCount > 0 ? Entities.BatchJobStatus.CompletedWithFailures : Entities.BatchJobStatus.Completed;
                batchJob.CompletedAt = DateTime.UtcNow;
            }
            await context.SaveChangesAsync();
        }
    }
}