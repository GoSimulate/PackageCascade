namespace PackageCascade.Public.Planning.Definition;

/// <summary>Packages to release, calculated from a specific repository commit and build configuration.</summary>
/// <param name="Packages">The selected packages in dependency-first order.</param>
/// <param name="TargetCommitSha">The full SHA of the repository HEAD used to create this plan.</param>
/// <param name="Configuration">The MSBuild configuration used to discover projects and dependencies.</param>
public sealed record ReleasePlan(IReadOnlyList<PlannedRelease> Packages, string TargetCommitSha, string Configuration);
