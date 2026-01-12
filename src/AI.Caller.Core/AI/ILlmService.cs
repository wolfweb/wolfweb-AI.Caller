using AI.Caller.Core.Models;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AI.Caller.Core.AI {
    /// <summary>
    /// LLM 抽象：提供意图分类、同步/流式回复生成接口
    /// </summary>
    public interface ILlmService {
        /// <summary>
        /// 简单意图分类（PoC）
        /// </summary>
        Task<IntentResult> ClassifyIntentAsync(string transcript, IEnumerable<ChatMessage>? history = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 流式生成（可逐步返回片段），PoC 可一次性返回完整文本
        /// </summary>
        IAsyncEnumerable<string> StreamGenerateAsync(IEnumerable<ChatMessage> messages, float? temperature = null, CancellationToken cancellationToken = default);
    }
}
