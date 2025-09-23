using AI.Caller.Core;
using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.CallRouting.Models {
    /// <summary>
    /// 通话路由结果
    /// </summary>
    public class CallRoutingResult {
        /// <summary>
        /// 路由是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 结果消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 呼叫来源号码
        /// </summary>
        public string? CallerNumber { get; set; } 

        /// <summary>
        /// 呼叫用户
        /// </summary>
        public User? CallerUser { get; set; }

        /// <summary>
        /// 目标用户
        /// </summary>
        public User? TargetUser { get; set; }

        /// <summary>
        /// 处理策略
        /// </summary>
        public CallHandlingStrategy Strategy { get; set; } = CallHandlingStrategy.Reject;

        /// <summary>
        /// 创建成功的路由结果
        /// </summary>
        public static CallRoutingResult CreateSuccess(User? targetUser, CallHandlingStrategy strategy, string message = "路由成功") {
            return new CallRoutingResult {
                Success = true,
                Message = message,
                Strategy = strategy,
                TargetUser = targetUser,
            };
        }

        /// <summary>
        /// 创建失败的路由结果
        /// </summary>
        public static CallRoutingResult CreateFailure(string message, CallHandlingStrategy strategy = CallHandlingStrategy.Reject) {
            return new CallRoutingResult {
                Success = false,
                Message = message,
                Strategy = strategy
            };
        }
    }
}