using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranslatorGUI.Models
{
    [JsonConverter(typeof(ModIdNameMapEntryConverter))]
    public sealed class ModIdNameMapEntry
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("total_priority")]
        public double? TotalPriority { get; set; }
    }

    internal sealed class ModIdNameMapEntryConverter : JsonConverter<ModIdNameMapEntry>
    {
        public override ModIdNameMapEntry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                reader.Read();
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                using var _ = JsonDocument.ParseValue(ref reader);
                return new ModIdNameMapEntry();
            }

            string? name = null;
            double? totalPriority = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    using var _ = JsonDocument.ParseValue(ref reader);
                    continue;
                }

                var prop = reader.GetString();
                if (!reader.Read()) break;

                switch (prop)
                {
                    case "name":
                        name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                        break;

                    case "total_priority":
                        totalPriority = reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var d) ? d : (double?)null;
                        break;

                    default:
                        using (var _ = JsonDocument.ParseValue(ref reader)) { }
                        break;
                }
            }

            return new ModIdNameMapEntry { Name = name, TotalPriority = totalPriority };
        }

        public override void Write(Utf8JsonWriter writer, ModIdNameMapEntry value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value, options);
    }
}
