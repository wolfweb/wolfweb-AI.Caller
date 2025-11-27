using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using Microsoft.AspNetCore.SignalR;
using SIPSorcery.SIP.App;
using System.Threading.Channels;

namespace AI.Caller.Phone.Services {
    public class SipService {
        private readonly ILogger _logger;        
        private readonly Channel<User> _channel;

        public SipService(
            ILogger<SipService> logger,            
            Channel<User> channel            
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
                _channel.Writer.TryWrite(user);                
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, $"用户 {user.Username} 的SIP账号注册失败");
                return false;
            }
        }

        
    }
}