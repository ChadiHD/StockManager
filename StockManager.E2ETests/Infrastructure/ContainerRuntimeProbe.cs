using System.Diagnostics;

namespace StockManager.E2ETests.Infrastructure;

/// <summary>
/// Whether a container runtime will answer at all, checked before asking Aspire to provision
/// SQL Server in one.
/// </summary>
/// <remarks>
/// The developer this project was built for runs Podman Desktop, not Docker, so "docker" is
/// tried first only because that is Aspire's own default -- "podman" is tried just as readily,
/// and either answering is enough. Without this check, a machine with neither running would
/// sit out the whole of AspireAppFixture's generous resource-healthy timeout before any test
/// could report anything; this turns that into a fast, specific skip instead.
/// </remarks>
public static class ContainerRuntimeProbe
{
    private static readonly string[] Candidates = ["docker", "podman"];

    public static async Task<bool> IsAnyRuntimeAvailableAsync(CancellationToken cancellationToken = default)
    {
        foreach (var candidate in Candidates)
        {
            if (await RespondsAsync(candidate, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> RespondsAsync(string executable, CancellationToken cancellationToken)
    {
        Process? process = null;

        try
        {
            process = Process.Start(new ProcessStartInfo(executable, "version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return false;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            await process.WaitForExitAsync(linked.Token);

            return process.ExitCode == 0;
        }
        catch
        {
            // Not installed, not on PATH, or it did not answer within the timeout -- all the
            // same conclusion here: this is not a runtime the fixture can use.
            return false;
        }
        finally
        {
            TryKill(process);
        }
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup of a diagnostic probe; nothing downstream depends on this.
        }
    }
}
