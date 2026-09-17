using System.Diagnostics;
using System.Security.Principal;

namespace InstantFileSearch;

/// <summary>
/// Elevation is opt-in. Scan never raises UAC; File → Run as administrator relaunches once.
/// </summary>
public static class ProcessElevation
{
    public static bool IsCurrentProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or SystemException)
        {
            return false;
        }
    }

    public static string WindowTitle(bool elevated, bool editMode)
    {
        var title = "Instant File Search";
        if (elevated)
        {
            title += " — Administrator";
        }

        if (editMode)
        {
            title += " — Edit";
        }

        return title;
    }

    public static string PrivilegeLabel(bool elevated) => elevated ? "Administrator" : "";

    public static string RunAsAdministratorTip(bool elevated) =>
        elevated
            ? "Already running as administrator."
            : "Relaunch Instant File Search elevated. Needed for admin shares such as \\\\SERVER\\C$. Scan itself does not prompt for UAC.";

    public static ProcessStartInfo RelaunchStartInfo(string exePath, string? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        return new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments ?? "",
            UseShellExecute = true,
            Verb = "runas",
        };
    }
}
