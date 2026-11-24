using System.Diagnostics;

namespace Grimoire.Cli;

internal interface IProcessRunner
{
    /// <summary>Runs the command to completion and returns its exit code.</summary>
    Task<int> RunAsync(ProcessStartInfo startInfo, CancellationToken ct);
}

/// <summary>
/// Starts the child with the console attached (no redirection), so it reads and writes the
/// terminal directly. Ctrl+C reaches the child through the terminal; the CLI only waits.
/// </summary>
internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(ProcessStartInfo startInfo, CancellationToken ct)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{startInfo.FileName}'.");

        // Ctrl+C goes to the whole process group: let the child decide how to stop, and keep
        // waiting so its exit code is the CLI's.
        ConsoleCancelEventHandler keepWaiting = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += keepWaiting;
        try
        {
            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= keepWaiting;
        }
    }

    /// <summary>
    /// The start info for <paramref name="command"/> with <paramref name="variables"/> added to
    /// the inherited environment. The CLI's own credentials are not passed on.
    /// </summary>
    public static ProcessStartInfo Build(
        IReadOnlyList<string> command,
        IReadOnlyDictionary<string, string> variables
    )
    {
        var startInfo = new ProcessStartInfo(command[0]) { UseShellExecute = false };
        foreach (var arg in command.Skip(1))
            startInfo.ArgumentList.Add(arg);
        startInfo.Environment.Remove(CliHost.TokenVariable);
        foreach (var (name, value) in variables)
            startInfo.Environment[name] = value;
        return startInfo;
    }
}
