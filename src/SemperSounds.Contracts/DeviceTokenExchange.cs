namespace SemperSounds.Contracts;

/// <summary>The desktop client redeeming its one-time pairing code for a device token.</summary>
/// <param name="Verifier">
/// The secret half of the challenge published when the flow started. Any local process can
/// watch the loopback port and race for the code; only the app that began pairing has this.
/// </param>
public sealed record DeviceTokenRequest(string Code, string Verifier, string RedirectUri);

/// <param name="UserId">A string: a Discord snowflake exceeds what a JSON number holds exactly.</param>
public sealed record DeviceTokenResponse(string Token, string UserId, string UserName);

/// <summary>A refusal the client can show the user verbatim.</summary>
public sealed record DeviceTokenError(string Error);
