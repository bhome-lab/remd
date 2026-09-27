using RemoteControl.Core.Input;
using RemoteControl.Infrastructure.Desktop;
using RemoteControl.Infrastructure.Execution;
using RemoteControl.Infrastructure.Input;
using RemoteControl.Infrastructure.Scripting;
using RemoteControl.Infrastructure.Transport;

namespace RemoteControl.Service;

internal static class WorkerApplication
{
    public static async Task RunAsync(string pipeName, CancellationToken cancellationToken)
    {
        using var device = new FakerInputDevice();
        using var input = new InputService(device, new WindowsKeyboardLayout(), new WindowsDesktopPointer());
        var screen = new WindowsScreenService();
        using var environments = new EnvironmentService();
        await using var execution = new ExecutionService(environments);
        using var lua = new LuaService(execution, input, environments);
        var dispatcher = new WorkerDispatcher(input, screen, execution, environments, lua);
        var server = new WorkerPipeServer(pipeName, dispatcher.DispatchAsync);
        await server.RunAsync(cancellationToken);
    }
}

