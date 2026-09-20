namespace SemperSounds.Contracts;

/// <summary>A clip that is sounding in the channel right now, and who set it off.</summary>
/// <remarks>
/// The attribution cannot be derived from the mixer, which knows only which audio is playing.
/// It is remembered when the play is accepted and forgotten when the clip drops out of the
/// mixer, so the two always describe the same moment.
/// </remarks>
/// <param name="IsEntrySound">
/// True when somebody walked into the channel rather than pressed anything. Worth telling
/// apart: "alkobottle played Airhorn" and "Airhorn played because alkobottle arrived" are
/// different events, and only one of them is somebody doing something.
/// </param>
public sealed record NowPlaying(Guid SoundId, string SoundName, string PlayedBy, bool IsEntrySound);
