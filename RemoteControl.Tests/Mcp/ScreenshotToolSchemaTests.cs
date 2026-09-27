using RemoteControl.Service.Mcp;

namespace RemoteControl.Tests.Mcp;

public sealed class ScreenshotToolSchemaTests
{
    [Fact]
    public void AllScreenshotToolsExposeOptionalEncodingControls()
    {
        var tools = ComputerToolCatalog.CreateTools()
            .Where(tool => tool.ProtocolTool.Name.StartsWith("computer.screenshot", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(4, tools.Length);

        foreach (var tool in tools)
        {
            var schema = tool.ProtocolTool.InputSchema;
            var properties = schema.GetProperty("properties");
            foreach (var parameter in new[] { "format", "quality", "maxWidth", "maxHeight", "maxBytes" })
            {
                Assert.True(properties.TryGetProperty(parameter, out _), $"{tool.ProtocolTool.Name} lacks {parameter}");
                if (schema.TryGetProperty("required", out var required))
                    Assert.DoesNotContain(required.EnumerateArray(), item => item.GetString() == parameter);
            }
        }
    }
}
