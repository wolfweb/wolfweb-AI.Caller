using System;

namespace AI.Caller.Core
{
    /// <summary>
    /// Configuration model for ICE servers that supports proper configuration binding.
    /// This class uses properties instead of fields to enable ASP.NET Core configuration binding.
    /// </summary>
    public class IceServerConfig
    {
        /// <summary>
        /// Array of server URLs. Supports both STUN and TURN server URLs.
        /// Examples: ["stun:stun.example.com:19302"], ["turn:turn.example.com:3478"]
        /// </summary>
        public string? Urls { get; set; }

        /// <summary>
        /// Username for authentication with TURN servers.
        /// Required for TURN servers, not used for STUN servers.
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// Credential (password) for authentication with TURN servers.
        /// Required for TURN servers, not used for STUN servers.
        /// </summary>
        public string? Credential { get; set; }
    }
}