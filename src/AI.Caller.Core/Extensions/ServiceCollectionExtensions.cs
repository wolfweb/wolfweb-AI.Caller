using AI.Caller.Core.Interfaces;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AI.Caller.Core.Extensions {
    public static class ServiceCollectionExtensions {
        public static IServiceCollection AddAIAutoResponder(this IServiceCollection services) {            
            services.TryAddSingleton<ITTSEngine, TTSEngineAdapter>();
            services.TryAddSingleton<IAIAutoResponderFactory, AIAutoResponderFactory>();

            services.TryAddTransient<IAudioBridge, AudioBridge>();
            return services;
        }

        public static IServiceCollection AddMediaProcessing(this IServiceCollection services) {
            services.TryAddTransient<IAudioBridge, AudioBridge>();
            return services;
        }
    }
}