using AI.Caller.Core;
using AI.Caller.Core.Models;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;

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
        var aiManager = scope.ServiceProvider.GetRequiredService<AICustomerServiceManager>();

        var callLog = await context.CallLogs.FindAsync(callLogId);
        if (callLog == null) {
            _logger.LogWarning("CallLog with ID {CallLogId} not found for processing.", callLogId);
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

        try {
            Action<SIPClient> callAnsweredHandler = null!;
            Action<SIPClient, HangupEventContext> callFinishedHandler = null!;

            callAnsweredHandler = (sc) => {
                _logger.LogInformation("Call answered for CallLogId {CallLogId}.", callLogId);
                callWasAnswered = true;
            };

            callFinishedHandler = (sc, hangupContext) => {
                sc.CallAnswered -= callAnsweredHandler;
                sc.CallFinishedWithContext -= callFinishedHandler;

                if (callWasAnswered) {
                    tcs.TrySetResult(new CallResult { Status = CallOutcome.Completed });
                } else {
                    tcs.TrySetResult(new CallResult { Status = CallOutcome.Failed, FailureReason = "Call was not answered." });
                }
            };

            var webUser = await context.Users.Include(x => x.SipAccount).FirstOrDefaultAsync(x => x.SipAccount != null && x.SipAccount.SipUsername == callLog.PhoneNumber);
            var callContext = await callManager.MakeCallAsync(callLog.PhoneNumber, agent, null, webUser != null ? CallScenario.ServerToWeb : CallScenario.ServerToMobile);

            if (callContext?.Caller?.Client?.Client == null) {
                throw new InvalidOperationException("Failed to create a valid call context or SIP client from CallManager.");
            }
            sipClient = callContext.Caller.Client.Client;

            sipClient.CallAnswered += callAnsweredHandler;
            sipClient.CallFinishedWithContext += callFinishedHandler;

            _logger.LogInformation("Starting AI Customer Service for CallLogId {CallLogId}.", callLogId);
            bool aiStarted = await aiManager.StartAICustomerServiceAsync(agent, sipClient, callLog.ResolvedContent);

            if (!aiStarted) {
                throw new InvalidOperationException("Failed to start AI Customer Service. The call will be terminated.");
            }

            _logger.LogInformation("Waiting for call completion for CallLogId {CallLogId}.", callLogId);
            var callResult = await tcs.Task;

            if (callResult.Status == CallOutcome.Completed) {
                callLog.Status = Entities.CallStatus.Completed;
                _logger.LogInformation("Call to {PhoneNumber} completed successfully for CallLogId {CallLogId}.", callLog.PhoneNumber, callLogId);
            } else {
                callLog.Status = Entities.CallStatus.Failed;
                callLog.FailureReason = callResult.FailureReason ?? "Unknown failure";
                _logger.LogWarning("Call to {PhoneNumber} failed for CallLogId {CallLogId}: {FailureReason}", callLog.PhoneNumber, callLogId, callLog.FailureReason);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "An error occurred while processing CallLogId {CallLogId}.", callLogId);
            callLog.Status = Entities.CallStatus.Failed;
            callLog.FailureReason = $"An unexpected error occurred: {ex.Message}";
            sipClient?.Hangup();
        } finally {
            if (agent != null) {
                _logger.LogInformation("Stopping AI Customer Service for agent {AgentId} for CallLogId {CallLogId}.", agent.Id, callLogId);
                await aiManager.StopAICustomerServiceAsync(agent.Id);
            }

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