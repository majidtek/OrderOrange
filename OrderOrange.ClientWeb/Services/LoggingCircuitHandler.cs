using Microsoft.AspNetCore.Components.Server.Circuits;

namespace OrderOrange.ClientWeb.Services;

/// <summary>
/// Writes circuit lifetime events to the log. Without this, a Blazor circuit that dies
/// shows the user "An unhandled error has occurred" while the server records nothing
/// useful, which leaves the actual cause to guesswork.
/// </summary>
public sealed class LoggingCircuitHandler(ILogger<LoggingCircuitHandler> log) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken ct)
    {
        log.LogInformation("Circuit {Id} opened.", circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken ct)
    {
        log.LogInformation("Circuit {Id} closed.", circuit.Id);
        return Task.CompletedTask;
    }
}
