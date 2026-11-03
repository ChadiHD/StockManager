using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using StockApi.Controllers;

namespace StockApi.Tests.TestSupport;

// Runs Program up to builder.Build() and aborts it there, the way dotnet-ef and
// WebApplicationFactory reach a minimal-hosting app's container: everything after Build
// migrates a database and seeds an admin, and the tests using this need neither. Development,
// because that is where ValidateOnBuild checks every registration's own dependencies.
public static class ProgramHost
{
    public static IHost Build()
    {
        IHost? host = null;
        var thread = Environment.CurrentManagedThreadId;

        // Other test classes build hosts in parallel; only the one built on this thread is ours.
        using var subscription = DiagnosticListener.AllListeners.Subscribe(new Observer<DiagnosticListener>(listener =>
        {
            if (listener.Name != "Microsoft.Extensions.Hosting")
            {
                return;
            }

            listener.Subscribe(new Observer<KeyValuePair<string, object?>>(diagnostic =>
            {
                if (diagnostic.Key == "HostBuilt" && Environment.CurrentManagedThreadId == thread)
                {
                    host = (IHost)diagnostic.Value!;
                    throw new HostAbortedException();
                }
            }));
        }));

        string[] args =
        [
            "--environment=Development",
            // The test runner is the entry assembly, and controller discovery starts from this name.
            "--applicationName=StockApi",
            "--Jwt:SigningKey=program-host-test-signing-key-0123456789abcdef",
        ];

        try
        {
            var result = typeof(TokenController).Assembly.EntryPoint!.Invoke(null, [args]);
            (result as Task)?.GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is HostAbortedException || exception.InnerException is HostAbortedException)
        {
        }

        return host ?? throw new InvalidOperationException("Program returned without building a host.");
    }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
