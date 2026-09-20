using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace SemperSounds.Desktop.Services;

/// <summary>
/// Whether the app can see the keys the foreground window is receiving.
/// </summary>
/// <remarks>
/// By UIPI, a process cannot observe input destined for a window of higher integrity. A
/// non-elevated keyboard hook therefore goes completely quiet whenever an elevated window is in
/// front — and games with kernel anti-cheat commonly run elevated. The user's experience is
/// "my hotkeys work on the desktop and do nothing in the game", with no error anywhere, so the
/// app has to be able to name the cause.
/// </remarks>
public static partial class Elevation
{
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// True when the window in front belongs to a process this one cannot read input for.
    /// </summary>
    /// <remarks>
    /// Inferred from being denied access to the process rather than from asking about its
    /// integrity level directly: that denial is the same condition that blinds the hook, so it
    /// is the honest test. Anything unexpected answers "no", because a false alarm telling the
    /// user to run as administrator is worse than staying quiet.
    /// </remarks>
    public static bool ForegroundWindowIsOutOfReach()
    {
        if (IsElevated)
        {
            return false;
        }

        try
        {
            var window = GetForegroundWindow();
            if (window == 0)
            {
                return false;
            }

            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == 0)
            {
                return false;
            }

            using var process = Process.GetProcessById((int)processId);
            _ = process.MainModule;
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Restarts this app with a UAC prompt. False when the user declined.
    /// </summary>
    /// <remarks>
    /// Offered as a button rather than made permanent through a scheduled task: running a
    /// tray app as administrator for every session, forever, to fix something that may not
    /// affect the user's games at all is a poor trade. This way the cost is one prompt on the
    /// days it is needed.
    /// </remarks>
    public static bool RelaunchElevated()
    {
        var executable = Environment.ProcessPath;
        if (executable is null)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Exception)
        {
            // The user dismissed the UAC prompt, which is an answer rather than a fault.
            return false;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);
}

/// <summary>Starting with Windows, through the per-user Run key.</summary>
/// <remarks>
/// Read through rather than cached: Task Manager's startup tab can disable an entry behind the
/// app's back, and a checkbox that disagrees with reality is worse than no checkbox.
/// </remarks>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SemperSounds";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
            {
                return;
            }

            if (enabled && Environment.ProcessPath is { } path)
            {
                key.SetValue(ValueName, $"\"{path}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception)
        {
            // A locked-down registry is the administrator's decision, not a bug to crash over.
        }
    }
}
