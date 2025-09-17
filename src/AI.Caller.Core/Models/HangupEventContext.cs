using SIPSorceryMedia.Abstractions;

namespace AI.Caller.Core.Models {
    public class HangupEventContext {
        public HangupInitiator Initiator { get; set; } = HangupInitiator.Unknown;
        public string Reason { get; set; } = string.Empty;

        public bool IsRemoteInitiated => Initiator == HangupInitiator.Remote;
    }
}