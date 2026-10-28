namespace PackageCascade.Public.Planning.Definition;

/// <summary>Packages to release from the repository's current HEAD.</summary>
/// <param name="Packages">The selected packages in dependency-first order.</param>
public sealed record ReleasePlan(IReadOnlyList<PlannedRelease> Packages);
