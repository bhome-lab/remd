using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace RemoteControl.Infrastructure.Execution;

internal sealed class ScriptSession : IRuntimeSession
{
    private readonly OwnedProcess _owner;
    private readonly NamedPipeServerStream _control;
    private readonly StreamReader _replyReader;
    private readonly StreamWriter _requestWriter;
    private readonly DelimitedOutputReader _stdout;
    private readonly DelimitedOutputReader _stderr;

    private ScriptSession(OwnedProcess owner, NamedPipeServerStream control)
    {
        _owner = owner;
        _control = control;
        _replyReader = new StreamReader(control, new UTF8Encoding(false), leaveOpen: true);
        _requestWriter = new StreamWriter(control, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        _stdout = new DelimitedOutputReader(owner.Process.StandardOutput);
        _stderr = new DelimitedOutputReader(owner.Process.StandardError);
    }

    public static async Task<IRuntimeSession> StartAsync(string runtime, string executable, string directory, CancellationToken cancellationToken)
    {
        var pipeName = "RemoteControl.Script." + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        OwnedProcess? owner = null;
        try
        {
            var info = ProcessRunner.StartInfo(executable, runtime == "python" ? ["-u", "-c", ScriptBootstraps.Python] : ["-e", ScriptBootstraps.Node], directory);
            info.RedirectStandardInput = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            info.Environment["PYTHONIOENCODING"] = "utf-8";
            info.Environment["REMOTE_CONTROL_SESSION_PIPE"] = @"\\.\pipe\" + pipeName;
            cancellationToken.ThrowIfCancellationRequested();
            owner = OwnedProcess.Start(info);
            using var stop = cancellationToken.Register(owner.Stop);
            await pipe.WaitForConnectionAsync(cancellationToken);
            return new ScriptSession(owner, pipe);
        }
        catch { owner?.Dispose(); pipe.Dispose(); throw; }
    }

    public async Task<RuntimeResult> ExecuteAsync(string code, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Guid.NewGuid().ToString("N");
        var marker = "\x1eREMOTE_CONTROL_END_" + id + "\x1f";
        using var stop = cancellationToken.Register(_owner.Stop);
        var stdoutTask = _stdout.ReadUntilAsync(marker, cancellationToken);
        var stderrTask = _stderr.ReadUntilAsync(marker, cancellationToken);
        var replyTask = ReadReplyAsync(id, cancellationToken);
        try
        {
            var request = JsonSerializer.Serialize(new ScriptRequest(id, code), SessionJsonContext.Default.ScriptRequest);
            await _requestWriter.WriteLineAsync(request.AsMemory(), cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask, replyTask);
            cancellationToken.ThrowIfCancellationRequested();
            var reply = await replyTask;
            return new(reply.Ok, await stdoutTask, await stderrTask, reply.Ok ? 0 : -1, reply.Error);
        }
        catch (Exception error)
        {
            _owner.Stop();
            _control.Dispose();
            await ProcessRunner.ObserveAsync(stdoutTask, stderrTask, replyTask);
            return new(false, _stdout.Partial, _stderr.Partial, -1,
                cancellationToken.IsCancellationRequested ? "execution_timeout" : error is EndOfStreamException ? "session_closed" : error.Message,
                SessionClosed: true);
        }
    }

    private async Task<ScriptReply> ReadReplyAsync(string id, CancellationToken cancellationToken)
    {
        var line = await _replyReader.ReadLineAsync(cancellationToken) ?? throw new EndOfStreamException();
        var reply = JsonSerializer.Deserialize(line, SessionJsonContext.Default.ScriptReply);
        if (reply is null || reply.Id != id) throw new InvalidDataException("invalid_session_response");
        return reply;
    }

    public void Dispose()
    {
        _owner.Dispose();
        _control.Dispose();
        _replyReader.Dispose();
        try { _requestWriter.Dispose(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private sealed class DelimitedOutputReader(StreamReader reader)
    {
        private string _pending = "";
        private StringBuilder _current = new();
        public string Partial => _current.ToString();

        public async Task<string> ReadUntilAsync(string marker, CancellationToken cancellationToken)
        {
            _current = new StringBuilder(_pending);
            _pending = "";
            var searchFrom = 0;
            var buffer = new char[4096];
            while (true)
            {
                var tail = _current.ToString(searchFrom, _current.Length - searchFrom);
                var markerOffset = tail.IndexOf(marker, StringComparison.Ordinal);
                if (markerOffset >= 0)
                {
                    var boundary = searchFrom + markerOffset;
                    _pending = _current.ToString(boundary + marker.Length, _current.Length - boundary - marker.Length);
                    _current.Length = boundary;
                    return _current.ToString();
                }
                searchFrom = Math.Max(0, _current.Length - marker.Length + 1);
                var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (count == 0) throw new EndOfStreamException();
                _current.Append(buffer, 0, count);
            }
        }
    }
}
