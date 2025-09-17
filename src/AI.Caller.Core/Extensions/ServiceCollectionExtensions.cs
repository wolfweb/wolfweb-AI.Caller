using AI.Caller.Core.Interfaces;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Adapters;
using AI.Caller.Core.Media.Interfaces;
using AI.Caller.Core.Media.Sources;
using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AI.Caller.Core.Extensions {
    public static class ServiceCollectionExtensions {
        public static IServiceCollection AddAIAutoResponder(this IServiceCollection services) {            
            services.TryAddSingleton<ITTSEngine, TTSEngineAdapter>();
            services.TryAddTransient<IVoiceActivityDetector, EnergyVad>();
            services.TryAddTransient<IAudioBridge, AudioBridge>();
            services.TryAddTransient<QueueAudioPlaybackSource>();
            services.TryAddTransient<IAIAutoResponderFactory, AIAutoResponderFactory>();
            return services;
        }

        public static IServiceCollection AddMediaProcessing(this IServiceCollection services) {
            services.TryAddTransient<IAudioBridge, AudioBridge>();
            return services;
        }
    }
}