using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using System.Threading.Channels;

namespace AI.Caller.Phone.Services {
    public class SipService {
        private readonly ILogger _logger;        
        private readonly Channel<SipRegisterModel> _channel;

        public SipService(
            ILogger<SipService> logger,            
            Channel<SipRegisterModel> channel
        ) {
            _logger = logger;
            _channel = channel;            
        }

        public async Task<bool> RegisterUserAsync(User user) {
            if (user.SipAccount == null || string.IsNullOrEmpty(user.SipAccount.SipUsername)) {
                _logger.LogWarning($"用户 {user.Username} 的SIP账号信息不完整");
                return false;
            }

            try {
                _channel.Writer.TryWrite(new SipRegisterModel(user.SipAccount, user));
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"用户 {user.Username} 的SIP账号注册失败");
                return false;
            }
        }        
    }
}