using NuGet.Versioning;

namespace PackageCascade.Public.Planning;

/// <summary>The proposed release of one packable project.</summary>
/// <param name="PackageId">The package's evaluated ID.</param>
/// <param name="ProjectPath">The project file path relative to the repository root, using forward slashes.</param>
/// <param name="PreviousVersion">The latest tagged version, or <see langword="null"/> for an initial release.</param>
/// <param name="Version">The proposed package version.</param>
/// <param name="PreviousTag">The latest release tag, or <see langword="null"/> for an initial release.</param>
/// <param name="Tag">The proposed release tag.</param>
/// <param name="Reasons">The commit, dependency, or initial-release reasons for this release.</param>
/// <param name="Dependencies">Direct in-solution package dependencies at their effective versions.</param>
public sealed record PlannedRelease(
	string PackageId,
	string ProjectPath,
	NuGetVersion? PreviousVersion,
	NuGetVersion Version,
	string? PreviousTag,
	string Tag,
	IReadOnlyList<ReleaseReason> Reasons,
	IReadOnlyList<PlannedDependency> Dependencies);
