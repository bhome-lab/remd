using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace RemoteControl.Service.Mcp;

public sealed class ControlTokenProvider
{
    public static string TokenFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RemoteControl", "control.token");

    public string Token { get; }

    public ControlTokenProvider(IConfiguration configuration)
    {
        var configured = configuration["Control:Token"] ?? Environment.GetEnvironmentVariable("REMOTE_CONTROL_TOKEN");
        Token = !string.IsNullOrWhiteSpace(configured) ? configured : LoadOrCreateToken();
    }

    private static string LoadOrCreateToken()
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Set Control:Token or REMOTE_CONTROL_TOKEN outside Windows.");

        var path = TokenFilePath;
        var directory = Path.GetDirectoryName(path)!;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var sidType in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sidType, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }
        var info = security.CreateDirectory(directory);
        info.SetAccessControl(security);

        if (!File.Exists(path))
        {
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            }
            catch (IOException) when (File.Exists(path)) { }
        }
        var token = File.ReadAllText(path).Trim();
        if (token.Length < 32) throw new InvalidOperationException("Invalid control token file: " + path);
        return token;
    }
}
