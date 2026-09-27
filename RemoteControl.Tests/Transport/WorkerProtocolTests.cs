using System.Text.Json;
using RemoteControl.Core;
using RemoteControl.Infrastructure.Transport;

namespace RemoteControl.Tests.Transport;

public sealed class WorkerProtocolTests
{
    [Fact]
    public void SequenceHoldAndResetFieldsRoundTripThroughWorkerJson()
    {
        var request = new WorkerRequest("sequence", new WorkerArgs(
            Actions: [new InputAction("key_down", Key: "CTRL", HoldTimeoutMs: 2000)],
            ResetBefore: true, ResetAfter: true));

        var json = JsonSerializer.Serialize(request, WorkerJsonContext.Default.WorkerRequest);
        using var document = JsonDocument.Parse(json);
        var args = document.RootElement.GetProperty("args");
        Assert.True(args.GetProperty("resetBefore").GetBoolean());
        Assert.True(args.GetProperty("resetAfter").GetBoolean());
        Assert.Equal(2000, args.GetProperty("actions")[0].GetProperty("holdTimeoutMs").GetInt32());

        var decoded = JsonSerializer.Deserialize(json, WorkerJsonContext.Default.WorkerRequest);
        Assert.True(decoded!.Args.ResetBefore);
        Assert.True(decoded.Args.ResetAfter);
        Assert.Equal(2000, decoded.Args.Actions![0].HoldTimeoutMs);
        Assert.Equal("CTRL", decoded.Args.Actions[0].Key?.Text);
    }
}
