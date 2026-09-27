using System.Diagnostics;
using RemoteControl.Infrastructure.Execution;
using Xunit;

namespace RemoteControl.Tests.Execution;

public sealed class ExecutionServiceTests
{
    [Theory]
    [InlineData("python", "import os,sys; print('first',flush=True); os.write(1,b'second\\n'); print('x'*131072); print('ошибка',file=sys.stderr)")]
    [InlineData("nodejs", "console.log('first'); process.stdout.write('second\\n'); await Promise.resolve(); console.log('x'.repeat(131072)); console.error('ошибка')")]
    public async Task Session_preserves_large_output_order_unicode_and_await(string runtime, string code)
    {
        using var environments = new EnvironmentService();
        await using var execution = new ExecutionService(environments);
        var result = await execution.RunAsync(runtime, code, null, "large", 5000, default);
        Assert.True(result.Ok, result.Error);
        var normalized = result.Stdout.Replace("\r\n", "\n");
        Assert.Equal("first\nsecond\n" + new string('x', 131072) + "\n", normalized);
        Assert.Equal("ошибка", result.Stderr.Trim());
    }

    [Theory]
    [InlineData("python", "x = 40; print('before'); raise ValueError('boom')", "print(x+2)")]
    [InlineData("nodejs", "let x = 40; console.log('before'); throw new Error('boom')", "console.log(x+2)")]
    public async Task Failed_command_preserves_output_and_persistent_state(string runtime, string failing, string next)
    {
        using var environments = new EnvironmentService();
        await using var execution = new ExecutionService(environments);
        var failed = await execution.RunAsync(runtime, failing, null, "state", 5000, default);
        Assert.False(failed.Ok);
        Assert.Contains("before", failed.Stdout);
        Assert.Contains("boom", failed.Stderr);
        var resumed = await execution.RunAsync(runtime, next, null, "state", 5000, default);
        Assert.True(resumed.Ok, resumed.Error);
        Assert.Equal("42", resumed.Stdout.Trim());
    }

    [Theory]
    [InlineData("python", "import os; os.write(1,b'__REMOTE_CONTROL_RESULT__plain log\\n'); print('real result')", "print('next result')")]
    [InlineData("nodejs", "process.stdout.write('__REMOTE_CONTROL_RESULT__plain log\\n'); console.log('real result')", "console.log('next result')")]
    public async Task Arbitrary_stdout_cannot_become_a_control_reply(string runtime, string first, string next)
    {
        using var environments = new EnvironmentService();
        await using var execution = new ExecutionService(environments);
        var result = await execution.RunAsync(runtime, first, null, "framing", 5000, default);
        Assert.True(result.Ok, result.Error);
        Assert.Equal("__REMOTE_CONTROL_RESULT__plain log\nreal result", result.Stdout.Trim().Replace("\r\n", "\n"));
        var again = await execution.RunAsync(runtime, next, null, "framing", 5000, default);
        Assert.True(again.Ok, again.Error);
        Assert.Equal("next result", again.Stdout.Trim());
    }

    [Fact]
    public async Task Timeout_covers_pipe_drain_and_terminates_child_after_parent_exit()
    {
        using var environments = new EnvironmentService();
        await using var execution = new ExecutionService(environments);
        var code = "import sys,subprocess; p=subprocess.Popen([sys.executable,'-c','import time; time.sleep(30)']); print(p.pid, flush=True)";
        var watch = Stopwatch.StartNew();
        var result = await execution.RunAsync("python", code, null, null, 1000, default);
        Assert.False(result.Ok);
        Assert.Equal("execution_timeout", result.Error);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
        var pid = int.Parse(result.Stdout.Trim());
        Assert.False(IsRunning(pid));
    }

    [Theory]
    [InlineData("python", "while True: pass", "print(42)")]
    [InlineData("nodejs", "while (true) {}", "console.log(42)")]
    public async Task Timed_out_session_is_recreated(string runtime, string endless, string next)
    {
        using var environments = new EnvironmentService();
        await using var execution = new ExecutionService(environments);
        var warmup = await execution.RunAsync(runtime, next, null, "timeout", 5000, default);
        Assert.True(warmup.Ok, warmup.Error);
        var stopped = await execution.RunAsync(runtime, endless, null, "timeout", 150, default);
        Assert.Equal("execution_timeout", stopped.Error);
        var resumed = await execution.RunAsync(runtime, next, null, "timeout", 5000, default);
        Assert.True(resumed.Ok, resumed.Error);
        Assert.Equal("42", resumed.Stdout.Trim());
    }

    [Fact]
    public async Task Omitted_environment_reuses_session_but_explicit_change_fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "remd-env-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var name in new[] { "a", "b" })
            {
                Directory.CreateDirectory(Path.Combine(root, name));
                await File.WriteAllTextAsync(Path.Combine(root, name, "package.json"), "{}");
            }
            using var environments = new EnvironmentService(root);
            await using var execution = new ExecutionService(environments);
            var first = await execution.RunAsync("nodejs", "let bound=42; console.log(process.cwd())", "a", "bound", 5000, default);
            Assert.True(first.Ok, first.Error);
            var omitted = await execution.RunAsync("nodejs", "console.log(bound); console.log(process.cwd())", null, "bound", 5000, default);
            Assert.True(omitted.Ok, omitted.Error);
            Assert.Contains("42", omitted.Stdout);
            Assert.Contains(Path.Combine(root, "a"), omitted.Stdout);
            var different = await execution.RunAsync("nodejs", "console.log('wrong')", "b", "bound", 5000, default);
            Assert.Equal("session_environment_mismatch", different.Error);
            Assert.True(await execution.CloseSessionAsync("nodejs", "bound", default));
            var reopened = await execution.RunAsync("nodejs", "console.log(process.cwd())", "b", "bound", 5000, default);
            Assert.True(reopened.Ok, reopened.Error);
            Assert.Contains(Path.Combine(root, "b"), reopened.Stdout);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PowerShell_commands_on_one_session_are_serialized()
    {
        using var environments = new EnvironmentService();
        await using var execution = new ExecutionService(environments);
        Assert.True((await execution.RunAsync("powershell", "$n=0", null, "parallel", 10000, default)).Ok);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => execution.RunAsync("powershell", "$n++; $n", null, "parallel", 10000, default)));
        Assert.All(results, result => Assert.True(result.Ok, result.Error));
        Assert.Equal(Enumerable.Range(1, 8), results.Select(x => int.Parse(x.Stdout.Trim())).Order());
    }

    private static bool IsRunning(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
