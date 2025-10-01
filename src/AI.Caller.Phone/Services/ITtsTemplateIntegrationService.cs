using AI.Caller.Phone.Entities;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Services;

/// <summary>
/// Provides a centralized service for integrating TTS templates and AI enablement logic.
/// </summary>
public interface ITtsTemplateIntegrationService
{
    /// <summary>
    /// Determines whether the AI service should be enabled for a call associated with a specific user.
    /// This decision is based on both global application settings and the user's individual preferences.
    /// </summary>
    /// <param name="user">The user associated with the call.</param>
    /// <returns>True if AI should be enabled for the call; otherwise, false.</returns>
    Task<bool> ShouldEnableAIForCall(User user);

    /// <summary>
    /// Gets the appropriate TTS script for a given call destination.
    /// It first tries to find a matching, active template in the database based on the destination and priority.
    /// If no specific template is found, it falls back to a default script.
    /// </summary>
    /// <param name="destination">The destination number of the call, used for template matching.</param>
    /// <returns>The content of the TTS script to be used for the call.</returns>
    Task<string> GetTtsScriptForCall(string destination);
}