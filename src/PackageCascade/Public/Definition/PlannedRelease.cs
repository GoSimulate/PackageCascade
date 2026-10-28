namespace PackageCascade.Public.Definition;

/// <summary>The proposed release of one packable project.</summary>
/// <param name="PackageId">The package's evaluated ID.</param>
/// <param name="ProjectPath">The project file path relative to the repository root, using forward slashes.</param>
/// <param name="Version">The proposed package version as a normalised string.</param>
/// <param name="Reasons">The commit, dependency, or initial-release reasons for this release.</param>
/// <param name="DependencyIds">The package IDs of directly referenced packable projects, including unchanged packages.</param>
public sealed record PlannedRelease(
	string PackageId,
	string ProjectPath,
	string Version,
	IReadOnlyList<ReleaseReason> Reasons,
	IReadOnlyList<string> DependencyIds);
