using System.Security.Cryptography;
using System.Text;
using SemperSounds.Desktop.Core;

namespace SemperSounds.Desktop.Services;

/// <summary>
/// Where the app keeps its settings and its credential on this machine.
/// </summary>
/// <remarks>
/// Both live under <c>%APPDATA%</c> rather than beside the executable, so the app can be a
/// single file the user drops anywhere — including somewhere they cannot write to.
/// </remarks>
public sealed class LocalStore
{
    private readonly string _directory;

    public LocalStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SemperSounds");

        Directory.CreateDirectory(_directory);
    }

    private string ConfigPath => Path.Combine(_directory, "config.json");

    private string TokenPath => Path.Combine(_directory, "token.bin");

    public DesktopConfig LoadConfig()
    {
        try
        {
            return DesktopConfig.Deserialize(File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : null);
        }
        catch (IOException)
        {
            // A locked or vanished file costs the user their settings for this run, not the
            // app's ability to start.
            return new DesktopConfig();
        }
    }

    /// <summary>
    /// Writes the config, replacing the previous one atomically.
    /// </summary>
    /// <remarks>
    /// Written to a temporary file and then swapped in, because the alternative is a window —
    /// however short — in which the real file is half a document. A crash or a power cut inside
    /// that window leaves unparseable JSON, and the app would come back with default bindings
    /// and no explanation.
    /// </remarks>
    public void SaveConfig(DesktopConfig config)
    {
        var temporary = ConfigPath + ".tmp";
        File.WriteAllText(temporary, config.Serialize(), Encoding.UTF8);

        if (File.Exists(ConfigPath))
        {
            // Keeps the previous version beside the new one, so a bad save is recoverable by
            // hand rather than only by re-binding everything.
            File.Replace(temporary, ConfigPath, ConfigPath + ".bak", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporary, ConfigPath);
        }
    }

    /// <summary>The stored device token, or null when this machine has never been paired.</summary>
    /// <remarks>
    /// Protected with DPAPI under the current user, so the file is useless to another account
    /// on the same machine and useless if copied off it. A failure to decrypt means "not
    /// paired" — the user pairs again, which costs them one browser round trip, rather than
    /// meeting a crash on startup.
    /// </remarks>
    public string? LoadToken()
    {
        try
        {
            if (!File.Exists(TokenPath))
            {
                return null;
            }

            var plaintext = ProtectedData.Unprotect(
                File.ReadAllBytes(TokenPath), optionalEntropy: null, DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            return null;
        }
    }

    public void SaveToken(string token) => File.WriteAllBytes(TokenPath, ProtectedData.Protect(
        Encoding.UTF8.GetBytes(token), optionalEntropy: null, DataProtectionScope.CurrentUser));

    public void ClearToken()
    {
        try
        {
            File.Delete(TokenPath);
        }
        catch (IOException)
        {
            // Unpairing locally is a convenience; the authority that matters was withdrawn on
            // the server. Failing here must not stop the rest of the sign-out.
        }
    }
}
