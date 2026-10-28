using System.Text.Json;
using System.Text.Json.Serialization;
using PackageCascade.Public.Definition;

namespace PackageCascade.Cli.Serialization;

internal sealed class ReleaseReasonJsonConverter : JsonConverter<ReleaseReason>
{
	public override ReleaseReason Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement value = document.RootElement;

		return value.GetProperty("Type").GetString() switch
		{
			"ConventionalCommit" => value.Deserialize<ConventionalCommitReason>(options)!,
			"DependencyRelease" => value.Deserialize<DependencyReleaseReason>(options)!,
			"InitialRelease" => new InitialReleaseReason(),
			_ => throw new JsonException("Unknown release reason type."),
		};
	}

	public override void Write(Utf8JsonWriter writer, ReleaseReason value, JsonSerializerOptions options)
	{
		switch (value)
		{
			case ConventionalCommitReason commit:
				WriteCase(writer, "ConventionalCommit", commit, options);
				break;

			case DependencyReleaseReason dependency:
				WriteCase(writer, "DependencyRelease", dependency, options);
				break;

			case InitialReleaseReason initial:
				WriteCase(writer, "InitialRelease", initial, options);
				break;

			default:
				throw new JsonException("Release reason has no active case.");
		}
	}

	private static void WriteCase<T>(Utf8JsonWriter writer, string type, T value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteString("Type", type);

		foreach (JsonProperty property in JsonSerializer.SerializeToElement(value, options).EnumerateObject())
		{
			property.WriteTo(writer);
		}

		writer.WriteEndObject();
	}
}
