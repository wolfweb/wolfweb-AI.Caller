using AI.Caller.Core.CallAutomation;
using AI.Caller.Core.Interfaces;
using AI.Caller.Core.Media;
using AI.Caller.Core.Media.Adapters;
using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AI.Caller.Core.Extensions {
    public static class ServiceCollectionExtensions {
        public static IServiceCollection AddAIAutoResponder(this IServiceCollection services) {            
            services.TryAddSingleton<G711Codec>();
            services.TryAddSingleton<ITTSEngine, TTSEngineAdapter>();
            services.TryAddSingleton<IDtmfService, DtmfService>();
            services.TryAddSingleton<IAIAutoResponderFactory, AIAutoResponderFactory>();
            services.TryAddScoped<AudioFilePlayer>();
            services.TryAddScoped<DtmfCollector>();

            services.TryAddTransient<IAudioBridge, AudioBridge>();
            return services;
        }
    }
}