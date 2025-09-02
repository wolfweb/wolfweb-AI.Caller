using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using AI.Caller.Phone.CallRouting.Interfaces;
using AI.Caller.Phone.CallRouting.Models;
using SIPSorcery.SIP;

namespace AI.Caller.Phone.CallRouting.Services {
    /// <summary>
    /// 呼叫类型识别服务实现
    /// </summary>
    public class CallTypeIdentifier : ICallTypeIdentifier {
        private readonly ConcurrentDictionary<string, OutboundCallInfo> _outboundCalls = new();
        private readonly ILogger<CallTypeIdentifier> _logger;
        private readonly Timer _cleanupTimer;

        public CallTypeIdentifier(ILogger<CallTypeIdentifier> logger) {
            _logger = logger;
            // 每5分钟清理一次已结束的呼叫记录
            _cleanupTimer = new Timer(CleanupCallback, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public OutboundCallInfo? GetOutboundCallInfo(string callId) {
            if (string.IsNullOrEmpty(callId))
                return null;

            _outboundCalls.TryGetValue(callId, out var callInfo);
            return callInfo;
        }

        public void RegisterOutboundCallWithSipTags(string callId, string fromTag, string sipUsername, string destination) {
            if (string.IsNullOrEmpty(callId) || string.IsNullOrEmpty(sipUsername)) {
                _logger.LogWarning("Cannot register outbound call with empty callId or sipUsername");
                return;
            }

            var callInfo = new OutboundCallInfo {
                CallId = callId,
                FromTag = fromTag,
                SipUsername = sipUsername,
                Destination = destination,
                Status = CallStatus.Initiated,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _outboundCalls.AddOrUpdate(callId, callInfo, (key, existing) => {
                existing.UpdatedAt = DateTime.UtcNow;
                return existing;
            });

            _logger.LogDebug($"Registered outbound call - CallId: {callId}, SipUsername: {sipUsername}, Destination: {destination}");
        }

        public void UpdateOutboundCallStatus(string callId, CallStatus status) {
            if (string.IsNullOrEmpty(callId))
                return;

            if (_outboundCalls.TryGetValue(callId, out var callInfo)) {
                callInfo.Status = status;
                callInfo.UpdatedAt = DateTime.UtcNow;
                _logger.LogDebug($"Updated outbound call status - CallId: {callId}, Status: {status}");
            }
        }

        public IEnumerable<OutboundCallInfo> GetActiveOutboundCalls() {
            return _outboundCalls.Values
                .Where(call => call.Status != CallStatus.Ended && call.Status != CallStatus.Failed)
                .ToList();
        }

        public void CleanupEndedCalls() {
            var cutoffTime = DateTime.UtcNow.AddHours(-1); // 清理1小时前结束的呼叫
            var keysToRemove = _outboundCalls
                .Where(kvp => (kvp.Value.Status == CallStatus.Ended || kvp.Value.Status == CallStatus.Failed)
                             && kvp.Value.UpdatedAt < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove) {
                _outboundCalls.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0) {
                _logger.LogDebug($"Cleaned up {keysToRemove.Count} ended call records");
            }
        }

        private void CleanupCallback(object? state) {
            try {
                CleanupEndedCalls();
            } catch (Exception ex) {
                _logger.LogError(ex, "Error during cleanup of ended calls");
            }
        }

        public void Dispose() {
            _cleanupTimer?.Dispose();
        }
    }
}