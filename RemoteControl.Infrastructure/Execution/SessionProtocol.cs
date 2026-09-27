using System.Text.Json.Serialization;

namespace RemoteControl.Infrastructure.Execution;

internal sealed record ScriptRequest(string Id, string Code);
internal sealed record ScriptReply(string Id, bool Ok, string? Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ScriptRequest))]
[JsonSerializable(typeof(ScriptReply))]
internal partial class SessionJsonContext : JsonSerializerContext;
