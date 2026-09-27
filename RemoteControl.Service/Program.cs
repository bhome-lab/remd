using ModelContextProtocol.AspNetCore;
using RemoteControl.Core;
using RemoteControl.Infrastructure.Transport;
using RemoteControl.Service;
using RemoteControl.Service.Hosting;
using RemoteControl.Service.Mcp;

if (args.Any(arg => string.Equals(arg, "--desktop-worker", StringComparison.OrdinalIgnoreCase)))
{
    var pipe = "RemoteControl.DesktopWorker";
    for (var i = 0; i + 1 < args.Length; i++)
        if (args[i] == "--pipe") pipe = args[i + 1];
    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
    try { await WorkerApplication.RunAsync(pipe, shutdown.Token); }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "RemoteControlSvc");

builder.Services.AddSingleton<ControlTokenProvider>();
builder.Services.AddSingleton<IWorkerProcessLauncher, UserWorkerProcessLauncher>();
builder.Services.AddSingleton<DesktopWorkerManager>();
builder.Services.AddSingleton<IWorkerEndpoint>(sp => sp.GetRequiredService<DesktopWorkerManager>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DesktopWorkerManager>());
builder.Services.AddSingleton<IWorkerConnection, WorkerPipeClient>();
builder.Services.AddSingleton<IInputService, WorkerInputService>();
builder.Services.AddSingleton<IScreenService, WorkerScreenService>();
builder.Services.AddSingleton<IExecutionService, WorkerExecutionService>();
builder.Services.AddSingleton<IEnvironmentService, WorkerEnvironmentService>();
builder.Services.AddSingleton<ILuaService, WorkerLuaService>();
builder.Services.AddMcpServer(options => options.ServerInstructions = ToolDocumentation.ServerInstructions)
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools(ComputerToolCatalog.CreateTools());

var app = builder.Build();
_ = app.Services.GetRequiredService<ControlTokenProvider>();
app.UseMiddleware<StaticTokenMiddleware>();
app.MapGet("/healthz", () => Results.Ok(new HealthStatus(true, "RemoteControlSvc", ".NET 10")));
app.MapMcp("/mcp");
app.Run();

public sealed record HealthStatus(bool Ok, string Service, string Runtime);
