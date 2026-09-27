using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using RemoteControl.Core;
using RemoteControl.Core.Input;

namespace RemoteControl.Service.Mcp;

/// <summary>Публикует фактические JSON-варианты типизированных клавиш в MCP schema.</summary>
public static class ComputerToolCatalog
{
    public static IEnumerable<McpServerTool> CreateTools()
    {
        foreach (var method in typeof(ComputerTools).GetMethods().Where(method => method.IsDefined(typeof(McpServerToolAttribute))))
        {
            yield return McpServerTool.Create(method,
                request => ActivatorUtilities.CreateInstance<ComputerTools>(request.Services!),
                new McpServerToolCreateOptions
                {
                    SchemaCreateOptions = new() { TransformSchemaNode = DescribeKeys }
                });
        }
    }

    private static JsonNode DescribeKeys(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        var type = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;
        if (type != typeof(KeyInput) && type != typeof(KeyChord)) return schema;
        var options = new JsonArray();
        if (type == typeof(KeyInput))
        {
            options.Add(new JsonObject { ["type"] = "string" });
            options.Add(new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 255 });
        }
        else
        {
            options.Add(new JsonObject { ["type"] = "string" });
            options.Add(new JsonObject { ["type"] = "array", ["minItems"] = 1, ["items"] = KeySchema() });
        }
        if (Nullable.GetUnderlyingType(context.TypeInfo.Type) is not null)
            options.Add(new JsonObject { ["type"] = "null" });
        var result = new JsonObject
        {
            ["anyOf"] = options,
            ["description"] = context.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? (type == typeof(KeyInput) ? ToolDocumentation.Key : ToolDocumentation.Keys)
        };
        if (type == typeof(KeyInput))
            result["description"] = result["description"]!.GetValue<string>() + "\n" + KeyNames;
        if (schema is JsonObject original && original.TryGetPropertyValue("default", out var defaultValue))
            result["default"] = defaultValue?.DeepClone();
        return result;
    }

    private static JsonObject KeySchema() => new()
    {
        ["anyOf"] = new JsonArray(
            new JsonObject { ["type"] = "string" },
            new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 255 }),
        ["description"] = ToolDocumentation.Key + "\n" + KeyNames
    };

    public static string KeyNames =>
        "Мнемоники (регистр не важен): F1..F24, KP0..KP9; " +
        string.Join(", ", KeyParser.NamedKeyNames) + "; " + string.Join(", ", KeyParser.ModifierNames) + ".";
}

