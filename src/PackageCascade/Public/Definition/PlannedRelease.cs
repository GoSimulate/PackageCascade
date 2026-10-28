namespace PackageCascade.Public.Definition;

/// <summary>The proposed release of one packable project.</summary>
/// <param name="PackageId">The package's evaluated ID.</param>
/// <param name="ProjectPath">The project file path; relative paths are resolved from the repository root.</param>
/// <param name="Version">The proposed package version as a normalised string.</param>
/// <param name="Reasons">The commit, dependency, or initial-release reasons for this release.</param>
/// <param name="DependencyIds">The package IDs expected as package dependencies in the manifest, including unchanged packages.</param>
public sealed record PlannedRelease(
	string PackageId,
	string ProjectPath,
	string Version,
	IReadOnlyList<ReleaseReason> Reasons,
	IReadOnlyList<string> DependencyIds);
