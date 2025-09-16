using System.Linq;
using AI.Caller.Core.Media.Tts;
using Xunit;

namespace AI.Caller.Core.Tests.Media.Tts {
    public class TtsVoiceRegistryTests {
        [Fact]
        public void VoiceRegistry_Has_CN_Voices() {
            var all = TtsVoiceRegistry.List().ToList();
            Assert.Contains(all, v => v.Id == "cn_female_01");
            Assert.Contains(all, v => v.Id == "cn_male_01");
        }

        [Fact]
        public void VoiceRegistry_TryGet_Works() {
            Assert.True(TtsVoiceRegistry.TryGet("cn_female_01", out var v));
            Assert.Equal("cn_female_01", v.Id);
        }
    }
}