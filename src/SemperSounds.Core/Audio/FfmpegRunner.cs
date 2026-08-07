using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SemperSounds.Core.Audio;

/// <summary>Raised when ffmpeg or ffprobe cannot be run or exits non-zero.</summary>
public sealed class FfmpegException(string message) : Exception(message);

/// <summary>
/// Runs an ffmpeg-family executable and captures its output.
/// Shared by the probe and the transcoder so process handling exists in one place.
/// </summary>
internal static class FfmpegRunner
{
    internal readonly record struct Result(int ExitCode, string StandardOutput, string StandardError);

    internal static async Task<Result> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Arguments are added individually rather than concatenated into a command line,
        // so a filename containing spaces or quotes cannot alter the invocation.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new FfmpegException(
                $"Could not start '{executable}'. Is ffmpeg installed and on PATH? ({ex.Message})");
        }

        // Read both streams concurrently: ffmpeg writes progress to stderr, and letting
        // either pipe fill while waiting on the other deadlocks the process.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process, logger);
            throw new FfmpegException($"'{executable}' did not finish within {timeout.TotalSeconds:0} seconds.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process, logger);
            throw;
        }

        return new Result(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void TryKill(Process process, ILogger logger)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to kill runaway process {ProcessName}", process.StartInfo.FileName);
        }
    }
}
