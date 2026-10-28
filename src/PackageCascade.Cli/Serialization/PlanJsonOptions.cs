using System.Text.Json;

namespace PackageCascade.Cli.Serialization;

internal static class PlanJsonOptions
{
	public static JsonSerializerOptions Options { get; } = new()
	{
		WriteIndented = true,
		Converters = { new ReleaseReasonJsonConverter() },
	};
}
