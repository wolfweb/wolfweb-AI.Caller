using AI.Caller.Core;
using AI.Caller.Core.Models;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Text.Json;

namespace AI.Caller.Phone.Services;

public class CallProcessor : ICallProcessor {
    private static readonly ConcurrentDictionary<int, int> _retryCounters = new();
    private const int MAX_RETRY_ATTEMPTS = 3;
    private const int RETRY_DELAY_SECONDS = 10;

    private const int MAX_RINGING_SECONDS = 60;       // 响铃/建立连接最大等待时间
    private const int MAX_CALL_DURATION_MINUTES = 30; // 通话最大允许时长

    private readonly ILogger<CallProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;

    public CallProcessor(ILogger<CallProcessor> logger, IServiceProvider serviceProvider) {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task ProcessCallLogJob(int callLogId, CancellationToken ct = default) {
        _logger.LogInformation("Starting to process CallLogId {CallLogId}.", callLogId);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var callManager = scope.ServiceProvider.GetRequiredService<ICallManager>();
        var ttsPlayer = scope.ServiceProvider.GetRequiredService<ITtsPlayerService>();
        var settingProvider = scope.ServiceProvider.GetRequiredService<IAICustomerServiceSettingsProvider>();
        var settings = await settingProvider.GetSettingsAsync();

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
            await HandleNoAgentAvailable(callLog, context);
            return;
        }

        _logger.LogInformation("Found idle agent '{AgentUsername}' (ID: {AgentId}) for CallLogId {CallLogId}.", agent.Username, agent.Id, callLogId);

        callLog.Status = Entities.CallStatus.InProgress;
        await context.SaveChangesAsync();

        var tcs = new TaskCompletionSource<CallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool callWasAnswered = false;
        SIPClient? sipClient = null;
        CallContext? callContext = null;

        Action<SIPClient> callAnsweredHandler = null!;
        Action<SIPClient, CallFinishStatus> callEndedHandler = null!;

        try {
            var webUser = await context.Users.Include(x => x.SipAccount).FirstOrDefaultAsync(x => x.SipAccount != null && x.SipAccount.SipUsername == callLog.CalleeNumber);
            
            int? selectedLineId = null;
            bool autoSelectLine = true;
            
            if (callLog.BatchCallJob != null) {
                selectedLineId = callLog.BatchCallJob.SelectedLineId;
                autoSelectLine = callLog.BatchCallJob.AutoSelectLine;
            }

            callLog.CallScenario = webUser != null ? CallScenario.ServerToWeb : CallScenario.ServerToMobile;

            callContext = await callManager.MakeCallAsync($"sip:{callLog.CalleeNumber}", agent, null, callLog.CallScenario.Value, selectedLineId, autoSelectLine);
            callLog.CallId = callContext.CallId;
            await context.SaveChangesAsync();

            callAnsweredHandler = async (sc) => {
                _logger.LogInformation("Call answered for CallLogId {CallLogId}. Starting AI Customer Service.", callLogId);
                callWasAnswered = true;
                await Task.Delay(1500);
                
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

                        var variables = JsonSerializer.Deserialize<Dictionary<string, string>>(callLog.ResolvedContent ?? "{}") ?? new Dictionary<string, string>();

                        if(await aiManager.StartScenarioServiceAsync(agent, sc, scenario, variables, callContext.CallId)) {
                            await aiManager.GetSessionByCallId(callContext.CallId)!.PlaybackTask!;
                        }
                        await aiManager.StopAICustomerServiceAsync(agent.Id);
                    } else {
                        var ttsTemplate = await context.TtsTemplates.FindAsync(batchCall!.TtsTemplateId);

                        await ttsPlayer.PlayTtsAsync(callLog.ResolvedContent ?? "", agent, sc, ttsTemplate?.SpeechRate, settings.DefaultSpeakerId);

                        if (ttsTemplate?.PlayCount > 1) {
                            for (var i = 0; i < ttsTemplate.PlayCount - 1; i++) {
                                await Task.Delay(ttsTemplate.PauseBetweenPlaysInSeconds * 1000);

                                await ttsPlayer.PlayTtsAsync(callLog.ResolvedContent ?? "", agent, sc, ttsTemplate.SpeechRate, settings.DefaultSpeakerId);
                            }
                        }

                        if (!string.IsNullOrEmpty(ttsTemplate?.EndingSpeech)) {
                            await Task.Delay(ttsTemplate.PauseBetweenPlaysInSeconds * 1000);
                            await ttsPlayer.PlayTtsAsync(ttsTemplate.EndingSpeech, agent, sc, ttsTemplate.SpeechRate, settings.DefaultSpeakerId);
                        }

                        await ttsPlayer.StopPlayoutAsync(agent);
                    }

                    tcs.TrySetResult(new CallResult { Status = CallOutcome.Completed });
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error starting AI Customer Service after call answered for CallLogId {CallLogId}.", callLogId);
                    tcs.TrySetResult(new CallResult { Status = CallOutcome.Failed, FailureReason = ex.Message });
                }
                await callManager.HangupCallAsync(callContext.CallId, callContext.Caller!.User!.Id);
            };
            callEndedHandler = (sc, status) => {
                sc.CallAnswered -= callAnsweredHandler;
                sc.CallEnding -= callEndedHandler;

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
            sipClient.CallEnding += callEndedHandler;

            //callContext.Caller!.Client!.Client.MediaSessionManager!.MediaConfigurationChanged += (codec, sampleRate, payloadType) => {
            //    _ = callContext.Callee!.Client!.Client.MediaSessionManager!.SwitchCodec(codec);
            //};

            _logger.LogInformation("Call initiated for CallLogId {CallLogId}. Waiting for answer to start AI service.", callLogId);


            var startTime = DateTime.UtcNow;
            bool hasConnectedOnce = false;

            while (!tcs.Task.IsCompleted) {
                if (ct.IsCancellationRequested) {
                    tcs.TrySetCanceled(ct);
                    break;
                }

                var checkInterval = Task.Delay(1000, ct);
                var completedTask = await Task.WhenAny(tcs.Task, checkInterval).ConfigureAwait(false);

                if (completedTask == tcs.Task) {
                    break;
                }

                
                var elapsed = DateTime.UtcNow - startTime;
                bool isActive = sipClient.IsCallActive;

                if (isActive) {
                    hasConnectedOnce = true;
                }

                if (elapsed.TotalMinutes > MAX_CALL_DURATION_MINUTES) {
                    _logger.LogWarning("CallLogId {CallLogId} reached max duration ({Minutes}m). Forcing completion.", callLogId, MAX_CALL_DURATION_MINUTES);

                    tcs.TrySetResult(new CallResult {
                        Status = CallOutcome.Failed,
                        FailureReason = "Max Duration Exceeded"
                    });

                    try { sipClient.Hangup(); } catch (Exception ex) { _logger.LogDebug(ex, "Hangup failed during max duration enforcement."); }

                    break;
                }

                if (!hasConnectedOnce && elapsed.TotalSeconds > MAX_RINGING_SECONDS) {
                    _logger.LogWarning("CallLogId {CallLogId} setup timed out after {Seconds}s (No Answer).", callLogId, MAX_RINGING_SECONDS);

                    tcs.TrySetResult(new CallResult {
                        Status = CallOutcome.NoAnswer,
                        FailureReason = "Setup Timeout (No Answer)"
                    });

                    break;
                }

                if (hasConnectedOnce && !isActive) {
                    _logger.LogInformation("CallLogId {CallLogId} physical connection lost. Waiting for event handler grace period...", callLogId);

                    var gracePeriod = Task.Delay(3000, ct);
                    var raceResult = await Task.WhenAny(tcs.Task, gracePeriod).ConfigureAwait(false);

                    if (raceResult == tcs.Task) {                        
                        continue;
                    } else {
                        _logger.LogWarning("CallLogId {CallLogId} event handler did not respond within grace period. Forcing completion.", callLogId);

                        tcs.TrySetResult(new CallResult {
                            Status = CallOutcome.Completed,
                            FailureReason = "Remote disconnected (Event Handler Stuck)"
                        });

                        break;
                    }
                }
            }

            CallResult callResult;
            try {
                callResult = await tcs.Task;
            } catch (TaskCanceledException) {
                _logger.LogInformation("CallLogId {CallLogId} was cancelled.", callLogId);
                callResult = new CallResult { Status = CallOutcome.Failed, FailureReason = "Operation Cancelled" };
            }
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
        } finally {
            callLog.CompletedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();

            if (callLog.BatchCallJobId.HasValue) {
                await UpdateBatchJobProgress(callLog.BatchCallJobId.Value);
            }

            if (sipClient != null) {
                sipClient.CallAnswered -= callAnsweredHandler;
                sipClient.CallEnding -= callEndedHandler;
                if (callContext != null && callContext.Caller != null && callContext.Caller.User != null) {
                    await callManager.HangupCallAsync(callContext.CallId, callContext.Caller.User.Id);
                }
            }
        }
    }

    private async Task HandleNoAgentAvailable(CallLog callLog, AppDbContext context) {
        var retryCount = _retryCounters.AddOrUpdate(callLog.Id, 1, (key, oldValue) => oldValue + 1);
        
        if (retryCount > MAX_RETRY_ATTEMPTS) {
            _logger.LogWarning("Max retry attempts ({MaxRetries}) reached for CallLogId {CallLogId}. Marking as failed.", MAX_RETRY_ATTEMPTS, callLog.Id);
            
            callLog.Status = Entities.CallStatus.Failed;
            callLog.FailureReason = $"No idle agent available after {MAX_RETRY_ATTEMPTS} retry attempts";
            callLog.CompletedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            
            if (callLog.BatchCallJobId.HasValue) {
                await UpdateBatchJobProgress(callLog.BatchCallJobId.Value);
            }
            
            _retryCounters.TryRemove(callLog.Id, out _);
            return;
        }
        
        _logger.LogInformation("No idle agent for CallLogId {CallLogId}. Retry attempt {RetryCount}/{MaxRetries} will be scheduled in {Delay} seconds.", callLog.Id, retryCount, MAX_RETRY_ATTEMPTS, RETRY_DELAY_SECONDS);
        
        var taskQueue = _serviceProvider.GetRequiredService<IBackgroundTaskQueue>();
        _ = Task.Run(async () => {
            await Task.Delay(TimeSpan.FromSeconds(RETRY_DELAY_SECONDS));
            
            taskQueue.QueueBackgroundWorkItem((token, serviceProvider) => {
                var callProcessor = serviceProvider.GetRequiredService<ICallProcessor>();
                return callProcessor.ProcessCallLogJob(callLog.Id);
            });
            
            _logger.LogInformation("CallLogId {CallLogId} has been re-queued for retry attempt {RetryCount}.", callLog.Id, retryCount);
        });
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