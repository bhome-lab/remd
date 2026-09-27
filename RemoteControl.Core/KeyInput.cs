using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteControl.Core;

/// <summary>Одна клавиша: строковое обозначение либо байт USB HID usage.</summary>
[JsonConverter(typeof(KeyInputJsonConverter))]
public readonly record struct KeyInput
{
    public string? Text { get; }
    public byte? Usage { get; }
    private KeyInput(string? text, byte? usage) { Text = text; Usage = usage; }
    public static KeyInput FromText(string text) => new(text, null);
    public static KeyInput FromUsage(byte usage) => new(null, usage);
    public static implicit operator KeyInput(string text) => FromText(text);
    public static implicit operator KeyInput(byte usage) => FromUsage(usage);
}

/// <summary>Сочетание клавиш: массив обозначений либо строка CTRL+A.</summary>
[JsonConverter(typeof(KeyChordJsonConverter))]
public readonly record struct KeyChord
{
    public string? Shortcut { get; }
    public KeyInput[]? Items { get; }
    private KeyChord(string? shortcut, KeyInput[]? items) { Shortcut = shortcut; Items = items; }
    public static KeyChord FromKeys(params KeyInput[] keys) => new(null, keys.ToArray());
    public static KeyChord FromShortcut(string shortcut) => new(shortcut, null);
    public static implicit operator KeyChord(string shortcut) => FromShortcut(shortcut);
}

public sealed class KeyInputJsonConverter : JsonConverter<KeyInput>
{
    public override KeyInput Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadValue(ref reader);

    internal static KeyInput ReadValue(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => KeyInput.FromText(reader.GetString()!),
        JsonTokenType.Number when reader.TryGetByte(out var usage) => KeyInput.FromUsage(usage),
        _ => throw new JsonException("key must be a string or an integer HID byte (0..255)")
    };

    public override void Write(Utf8JsonWriter writer, KeyInput value, JsonSerializerOptions options) => WriteValue(writer, value);

    internal static void WriteValue(Utf8JsonWriter writer, KeyInput value)
    {
        if (value.Usage is byte usage) writer.WriteNumberValue(usage);
        else if (value.Text is not null) writer.WriteStringValue(value.Text);
        else throw new JsonException("invalid_key");
    }
}

public sealed class KeyChordJsonConverter : JsonConverter<KeyChord>
{
    public override KeyChord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return KeyChord.FromShortcut(reader.GetString()!);
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("keys must be a key array or a shortcut string");
        var keys = new List<KeyInput>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return KeyChord.FromKeys(keys.ToArray());
            keys.Add(KeyInputJsonConverter.ReadValue(ref reader));
        }
        throw new JsonException("unterminated keys array");
    }

    public override void Write(Utf8JsonWriter writer, KeyChord value, JsonSerializerOptions options)
    {
        if (value.Shortcut is not null) { writer.WriteStringValue(value.Shortcut); return; }
        if (value.Items is null) throw new JsonException("invalid_key");
        writer.WriteStartArray();
        foreach (var key in value.Items) KeyInputJsonConverter.WriteValue(writer, key);
        writer.WriteEndArray();
    }
}

