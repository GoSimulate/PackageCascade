using NuGet.Versioning;

namespace PackageCascade.Public.Planning.Definition;

/// <summary>Package releases and dependency versions planned from the repository's current HEAD.</summary>
/// <param name="EffectiveVersions">Versions to use for selected packages and their packable dependencies, including dependencies that are not being released.</param>
/// <param name="Packages">The packages selected for release.</param>
/// <param name="BuildLayers">Package IDs grouped in dependency-first build order; packages within a layer have no dependencies on each other.</param>
public sealed record ReleasePlan(
	IReadOnlyDictionary<string, NuGetVersion> EffectiveVersions,
	IReadOnlyList<PlannedRelease> Packages,
	IReadOnlyList<IReadOnlyList<string>> BuildLayers);
