namespace PackageCascade.Public.Definition;

/// <summary>Packages to release, calculated for a build configuration.</summary>
/// <param name="Packages">The selected packages in dependency-first order.</param>
/// <param name="Configuration">The MSBuild configuration used to discover projects and dependencies.</param>
/// <param name="ReferencedPackages">Unchanged package projects needed to build selected releases, with their released versions.</param>
/// <param name="WorkingDirectory">The build working directory relative to the repository root, using forward slashes; <c>.</c> means the root.</param>
public sealed record ReleasePlan(
	IReadOnlyList<PlannedRelease> Packages,
	string Configuration,
	IReadOnlyList<ReferencedPackage> ReferencedPackages,
	string WorkingDirectory);
