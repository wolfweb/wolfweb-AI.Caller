using AI.Caller.Core.Interfaces;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Adapters;
using AI.Caller.Core.Media.Encoders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AI.Caller.Core.Extensions {
    public static class ServiceCollectionExtensions {
        public static IServiceCollection AddAIAutoResponder(this IServiceCollection services) {            
            services.TryAddSingleton<ITTSEngine, TTSEngineAdapter>();
            services.TryAddSingleton<G711Codec>();
            services.TryAddSingleton<IAIAutoResponderFactory, AIAutoResponderFactory>();

            services.TryAddTransient<IAudioBridge, AudioBridge>();
            return services;
        }
    }
}