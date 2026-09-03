using System.Text.Json;
using System.Text.Json.Serialization;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;

namespace Tappy.Core.Profiles;

public sealed class ProfileSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public string Serialize(TappyProfile profile) => Serialize(profile.CreateSnapshot());

    public string Serialize(TappyProfileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot.ToEditableProfile(), Options);
    }

    public TappyProfileSnapshot Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("The Tappy profile is empty.");
        }

        try
        {
            var profile = JsonSerializer.Deserialize<TappyProfile>(json, Options)
                ?? throw new InvalidDataException("The Tappy profile is invalid.");
            profile.Normalize();
            return profile.CreateSnapshot();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Tappy profile is invalid JSON.", exception);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ControllerSessionIdConverter());
        options.Converters.Add(new NullableControllerPersistentIdConverter());
        options.Converters.Add(new ControlIdConverter());
        options.Converters.Add(new KeyboardOutputKeyConverter());
        return options;
    }

    private sealed class ControllerSessionIdConverter : JsonConverter<ControllerSessionId>
    {
        public override ControllerSessionId Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(reader.GetString() ?? string.Empty);

        public override void Write(Utf8JsonWriter writer, ControllerSessionId value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    private sealed class NullableControllerPersistentIdConverter : JsonConverter<ControllerPersistentId>
    {
        public override ControllerPersistentId Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(reader.GetString() ?? string.Empty);

        public override void Write(Utf8JsonWriter writer, ControllerPersistentId value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    private sealed class ControlIdConverter : JsonConverter<ControlId>
    {
        public override ControlId Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(reader.GetString() ?? string.Empty);

        public override void Write(Utf8JsonWriter writer, ControlId value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    private sealed class KeyboardOutputKeyConverter : JsonConverter<KeyboardOutputKey>
    {
        public override KeyboardOutputKey Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(reader.GetString() ?? string.Empty);

        public override void Write(Utf8JsonWriter writer, KeyboardOutputKey value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }
}
