using SIPSorcery.SIP;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Phone.CallRouting.Helpers
{
    /// <summary>
    /// SIP对话辅助工具类
    /// </summary>
    public static class SipDialogueHelper
    {
        /// <summary>
        /// 从SIP请求中提取对话标识信息
        /// </summary>
        public static (string? CallId, string? FromTag, string? ToTag) ExtractDialogueInfo(SIPRequest sipRequest)
        {
            try
            {
                var callId = sipRequest.Header.CallId;
                var fromTag = sipRequest.Header.From?.FromTag;
                var toTag = sipRequest.Header.To?.ToTag;

                return (callId, fromTag, toTag);
            }
            catch
            {
                return (null, null, null);
            }
        }

        /// <summary>
        /// 从SIP响应中提取对话标识信息
        /// </summary>
        public static (string? CallId, string? FromTag, string? ToTag) ExtractDialogueInfo(SIPResponse sipResponse)
        {
            try
            {
                var callId = sipResponse.Header.CallId;
                var fromTag = sipResponse.Header.From?.FromTag;
                var toTag = sipResponse.Header.To?.ToTag;

                return (callId, fromTag, toTag);
            }
            catch
            {
                return (null, null, null);
            }
        }

        /// <summary>
        /// 生成符合SIP标准的From标签
        /// </summary>
        public static string GenerateFromTag()
        {
            // 使用时间戳和随机数生成唯一标签
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var random = new Random().Next(1000, 9999);
            return $"tag-{timestamp}-{random}";
        }

        /// <summary>
        /// 生成符合SIP标准的To标签
        /// </summary>
        public static string GenerateToTag()
        {
            // 使用GUID生成唯一标签
            return $"tag-{Guid.NewGuid():N}";
        }

        /// <summary>
        /// 验证SIP标签格式
        /// </summary>
        public static bool IsValidSipTag(string? tag)
        {
            if (string.IsNullOrEmpty(tag))
                return false;

            // SIP标签应该是token格式，不包含特殊字符
            return tag.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.');
        }

        /// <summary>
        /// 创建对话标识符
        /// </summary>
        public static string CreateDialogueId(string callId, string fromTag, string? toTag = null)
        {
            if (string.IsNullOrEmpty(toTag))
                return $"{callId}:{fromTag}";
            return $"{callId}:{fromTag}:{toTag}";
        }

        /// <summary>
        /// 解析对话标识符
        /// </summary>
        public static (string CallId, string FromTag, string? ToTag) ParseDialogueId(string dialogueId)
        {
            var parts = dialogueId.Split(':');
            if (parts.Length < 2)
                throw new ArgumentException("Invalid dialogue ID format");

            var callId = parts[0];
            var fromTag = parts[1];
            var toTag = parts.Length > 2 ? parts[2] : null;

            return (callId, fromTag, toTag);
        }

        /// <summary>
        /// 检查两个对话是否匹配
        /// </summary>
        public static bool DialoguesMatch(string callId1, string fromTag1, string? toTag1,
                                        string callId2, string fromTag2, string? toTag2)
        {
            // Call-ID必须匹配
            if (!callId1.Equals(callId2, StringComparison.OrdinalIgnoreCase))
                return false;

            // From-tag必须匹配
            if (!fromTag1.Equals(fromTag2, StringComparison.OrdinalIgnoreCase))
                return false;

            // 如果两个都有To-tag，则必须匹配
            if (!string.IsNullOrEmpty(toTag1) && !string.IsNullOrEmpty(toTag2))
            {
                return toTag1.Equals(toTag2, StringComparison.OrdinalIgnoreCase);
            }

            // 如果其中一个没有To-tag，认为匹配（可能是对话建立过程中）
            return true;
        }

        /// <summary>
        /// 从SIP用户代理事件中提取对话信息
        /// </summary>
        public static string? ExtractCallIdFromUserAgent(object userAgent)
        {
            try
            {
                // 这里需要根据实际的SIPUserAgent实现来提取Call-ID
                // 这是一个示例实现，需要根据具体的SIPSorcery API调整
                var userAgentType = userAgent.GetType();
                var callDescriptorProperty = userAgentType.GetProperty("CallDescriptor");
                
                if (callDescriptorProperty != null)
                {
                    var callDescriptor = callDescriptorProperty.GetValue(userAgent);
                    if (callDescriptor != null)
                    {
                        var callIdProperty = callDescriptor.GetType().GetProperty("CallId");
                        return callIdProperty?.GetValue(callDescriptor)?.ToString();
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 记录对话信息到日志
        /// </summary>
        public static void LogDialogueInfo(ILogger logger, string operation, string callId, string? fromTag, string? toTag, string? additionalInfo = null)
        {
            var dialogueId = CreateDialogueId(callId, fromTag ?? "unknown", toTag);
            var info = string.IsNullOrEmpty(additionalInfo) ? "" : $" - {additionalInfo}";
            logger.LogDebug($"{operation} - DialogueId: {dialogueId}{info}");
        }
    }
}