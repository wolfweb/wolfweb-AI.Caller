using AI.Caller.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AI.Caller.Core.AI {
    public class OllamaLlmService : ILlmService {
        private readonly ILogger<OllamaLlmService> _logger;
        private readonly OllamaSettings _settings;
        private readonly IOllamaApiClient _client;

        public OllamaLlmService(IOptions<OllamaSettings> options, ILogger<OllamaLlmService> logger) {            
            _logger = logger;
            _settings = options?.Value ?? new OllamaSettings();

            if (string.IsNullOrWhiteSpace(_settings.Model)) {
                _logger.LogWarning("Ollama model is not configured.");
            }

            try {
                _client = new OllamaApiClient(new Uri(_settings.BaseUrl), _settings.Model);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to initialize Ollama client");
                throw;
            }
        }

        public async Task<IntentResult> ClassifyIntentAsync(string transcript, IEnumerable<ChatMessage>? history = null, CancellationToken ct = default) {            
            var historyText = history != null
                ? string.Join("\n", history.TakeLast(2).Select(m => $"{m.Role}: {m.Text}"))
                : "";

            var systemPrompt = _settings.IntentTemplate ??
                @"You are an intent classifier for a customer service bot. 
              Analyze the user's input and history.
              Output ONLY a JSON object with the following fields:
              - label: The intent category (e.g., 'refund', 'order_status', 'transfer_human', 'greeting', 'unknown').
              - confidence: A number between 0.0 and 1.0.
              - reason: A brief explanation.";

            var fullPrompt = $"{systemPrompt}\n\n[History]\n{historyText}\n\n[User Input]\n{transcript}\n\nOutput JSON:";

            try {
                var req = new GenerateRequest {
                    Model = _settings.Model,
                    Prompt = fullPrompt,
                    Format = "json",
                    Stream = false,
                    Options = new RequestOptions { Temperature = 0.1f }
                };

                var sb = new StringBuilder();
                await foreach (var stream in _client.GenerateAsync(req, ct)) {
                    sb.Append(stream.Response);
                }

                var jsonResponse = sb.ToString();

                // 3. 反序列化结果
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<IntentResult>(jsonResponse, options);

                return result ?? new IntentResult { Label = "unknown", Confidence = 0 };
            } catch (JsonException) {
                _logger.LogWarning("Failed to parse intent JSON from LLM.");
                return new IntentResult { Label = "unknown", Confidence = 0, Reason = "JSON Parse Error" };
            } catch (Exception ex) {
                _logger.LogError(ex, "Intent classification failed");
                return new IntentResult { Label = "error", Confidence = 0 };
            }
        }

        public async IAsyncEnumerable<string> StreamGenerateAsync(
            IEnumerable<ChatMessage> messages,
            float? temperature = null,
            [EnumeratorCancellation] CancellationToken ct = default) {            
            var chatMessages = messages.Select(m => new Message {
                Role = ConvertRole(m.Role),
                Content = m.Text
            }).ToList();

            var req = new ChatRequest {
                Model = _settings.Model,
                Messages = chatMessages,
                Stream = true,
                Options = new RequestOptions {
                    Temperature = temperature ?? 0.7f
                }
            };

            IAsyncEnumerable<ChatResponseStream?>? stream = null;

            try {
                stream = _client.ChatAsync(req, ct);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to start chat stream");
                yield break;
            }

            if (stream != null) {
                await foreach (var chunk in stream) {
                    if (ct.IsCancellationRequested) yield break;

                    if (chunk.Message != null && !string.IsNullOrEmpty(chunk.Message.Content)) {
                        yield return chunk.Message.Content;
                    }
                }
            }
        }

        private OllamaSharp.Models.Chat.ChatRole ConvertRole(Microsoft.Extensions.AI.ChatRole role) {
            return role.Value.ToLower() switch {
                "system" => OllamaSharp.Models.Chat.ChatRole.System,
                "user" => OllamaSharp.Models.Chat.ChatRole.User,
                "assistant" => OllamaSharp.Models.Chat.ChatRole.Assistant,
                _ => OllamaSharp.Models.Chat.ChatRole.User
            };
        }

    }
}
