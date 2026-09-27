namespace RemoteControl.Infrastructure.Execution;

internal static class RuntimeNames
{
    public static string Normalize(string value) => value.Trim().ToLowerInvariant() switch
    {
        "pwsh" or "powershell" => "powershell",
        "node" or "nodejs" => "nodejs",
        "py" or "python" => "python",
        var other => other
    };

    public static string EnvironmentName(string value)
    {
        var name = value.Trim();
        if (name.Length == 0 || name.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("invalid_environment_name");
        return name;
    }
}
