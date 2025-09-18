using AI.Caller.Phone.Models;
using System.Text.RegularExpressions;

namespace AI.Caller.Phone.Services {
    /// <summary>
    /// TTS模板集成服务实现
    /// </summary>
    public class TtsTemplateIntegrationService : ITtsTemplateIntegrationService {
        private readonly IInboundTemplateService _templateService;
        private readonly ILogger<TtsTemplateIntegrationService> _logger;

        public TtsTemplateIntegrationService(
            IInboundTemplateService templateService,
            ILogger<TtsTemplateIntegrationService> logger) {
            _templateService = templateService;
            _logger = logger;
        }

        public async Task<string> GeneratePersonalizedScriptAsync(TtsCallRecord record) {
            try {
                var variables = ExtractVariables(record);
                var personalizedScript = ProcessTemplate(record.TtsContent, variables);
                
                _logger.LogDebug($"生成个性化脚本: {record.PhoneNumber} -> {personalizedScript}");
                return personalizedScript;
            } catch (Exception ex) {
                _logger.LogError(ex, $"生成个性化脚本失败: {record.PhoneNumber}");
                return record.TtsContent; // 返回原始内容作为备用
            }
        }

        public async Task<InboundTemplate?> GetOutboundTemplateAsync(int userId) {
            try {
                // 首先尝试获取默认模板
                var defaultTemplate = await _templateService.GetDefaultTemplateAsync(userId);
                if (defaultTemplate != null) {
                    return defaultTemplate;
                }

                // 如果没有默认模板，获取第一个活跃模板
                var templates = await _templateService.GetActiveTemplatesAsync(userId);
                return templates.FirstOrDefault();
            } catch (Exception ex) {
                _logger.LogError(ex, $"获取外呼模板失败: UserId={userId}");
                return null;
            }
        }

        public async Task<OutboundCallScript> PrepareOutboundScriptAsync(TtsCallRecord record, int userId) {
            try {
                var template = await GetOutboundTemplateAsync(userId);
                var variables = ExtractVariables(record);
                var personalizedContent = ProcessTemplate(record.TtsContent, variables);

                var script = new OutboundCallScript {
                    Record = record,
                    Template = template,
                    PersonalizedContent = personalizedContent,
                    Variables = variables
                };

                if (template != null) {
                    // 处理欢迎脚本中的变量
                    script.WelcomeScript = ProcessTemplate(template.WelcomeScript, variables);
                    
                    // 组合完整脚本：欢迎语 + 个性化内容
                    script.CombinedScript = CombineScripts(script.WelcomeScript, personalizedContent);
                } else {
                    // 没有模板时直接使用个性化内容
                    script.WelcomeScript = "您好，这里是AI客服。";
                    script.CombinedScript = script.WelcomeScript + " " + personalizedContent;
                }

                _logger.LogInformation($"准备外呼脚本完成: {record.PhoneNumber}");
                return script;
            } catch (Exception ex) {
                _logger.LogError(ex, $"准备外呼脚本失败: {record.PhoneNumber}");
                
                // 返回基础脚本作为备用
                return new OutboundCallScript {
                    Record = record,
                    PersonalizedContent = record.TtsContent,
                    WelcomeScript = "您好，这里是AI客服。",
                    CombinedScript = "您好，这里是AI客服。" + record.TtsContent,
                    Variables = ExtractVariables(record)
                };
            }
        }

        public bool ShouldEnableAICustomerService(TtsCallRecord record) {
            // 检查是否应该启用AI客服
            // 可以基于记录内容、时间、号码等条件判断
            
            // 简单实现：如果TTS内容包含交互关键词，启用AI客服
            var interactiveKeywords = new[] { 
                "请问", "咨询", "了解", "详情", "回复", "确认", 
                "选择", "按键", "转接", "人工", "客服" 
            };
            
            return interactiveKeywords.Any(keyword => 
                record.TtsContent.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private Dictionary<string, string> ExtractVariables(TtsCallRecord record) {
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["手机号"] = record.PhoneNumber,
                ["电话"] = record.PhoneNumber,
                ["号码"] = record.PhoneNumber,
                ["性别"] = record.Gender ?? "",
                ["称呼"] = record.AddressTemplate ?? "",
                ["称呼模板"] = record.AddressTemplate ?? ""
            };

            // 根据性别设置默认称呼
            if (string.IsNullOrEmpty(variables["称呼"])) {
                variables["称呼"] = record.Gender switch {
                    "男" => "先生",
                    "女" => "女士",
                    _ => "您"
                };
            }

            // 添加时间相关变量
            var now = DateTime.Now;
            variables["时间"] = now.ToString("HH:mm");
            variables["日期"] = now.ToString("yyyy年MM月dd日");
            variables["星期"] = GetChineseDayOfWeek(now.DayOfWeek);
            
            // 添加问候语
            variables["问候"] = GetTimeBasedGreeting(now.Hour);

            return variables;
        }

        private string ProcessTemplate(string template, Dictionary<string, string> variables) {
            if (string.IsNullOrEmpty(template)) {
                return template;
            }

            var result = template;
            
            // 处理 {变量名} 格式的占位符
            foreach (var variable in variables) {
                var placeholder = $"{{{variable.Key}}}";
                result = result.Replace(placeholder, variable.Value, StringComparison.OrdinalIgnoreCase);
            }

            // 处理 ${变量名} 格式的占位符
            foreach (var variable in variables) {
                var placeholder = $"${{{variable.Key}}}";
                result = result.Replace(placeholder, variable.Value, StringComparison.OrdinalIgnoreCase);
            }

            // 处理 [变量名] 格式的占位符
            foreach (var variable in variables) {
                var placeholder = $"[{variable.Key}]";
                result = result.Replace(placeholder, variable.Value, StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }

        private string CombineScripts(string welcomeScript, string personalizedContent) {
            if (string.IsNullOrEmpty(welcomeScript)) {
                return personalizedContent;
            }
            
            if (string.IsNullOrEmpty(personalizedContent)) {
                return welcomeScript;
            }

            // 智能组合脚本，避免重复的问候语
            var welcome = welcomeScript.Trim();
            var content = personalizedContent.Trim();

            // 如果个性化内容已经包含问候语，直接使用
            if (content.StartsWith("您好") || content.StartsWith("你好") || 
                content.StartsWith("Hello") || content.StartsWith("Hi")) {
                return content;
            }

            // 否则组合欢迎语和内容
            return $"{welcome} {content}";
        }

        private string GetChineseDayOfWeek(DayOfWeek dayOfWeek) {
            return dayOfWeek switch {
                DayOfWeek.Monday => "星期一",
                DayOfWeek.Tuesday => "星期二", 
                DayOfWeek.Wednesday => "星期三",
                DayOfWeek.Thursday => "星期四",
                DayOfWeek.Friday => "星期五",
                DayOfWeek.Saturday => "星期六",
                DayOfWeek.Sunday => "星期日",
                _ => ""
            };
        }

        private string GetTimeBasedGreeting(int hour) {
            return hour switch {
                >= 6 and < 12 => "早上好",
                >= 12 and < 14 => "中午好", 
                >= 14 and < 18 => "下午好",
                >= 18 and < 22 => "晚上好",
                _ => "您好"
            };
        }
    }
}