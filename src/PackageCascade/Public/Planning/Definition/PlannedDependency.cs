using NuGet.Versioning;

namespace PackageCascade.Public.Planning.Definition;

/// <summary>A direct in-solution package dependency expected in a planned release.</summary>
/// <param name="PackageId">The dependency's package ID.</param>
/// <param name="VersionRange">The inclusive minimum version range derived from the dependency's effective version.</param>
public sealed record PlannedDependency(string PackageId, VersionRange VersionRange);
