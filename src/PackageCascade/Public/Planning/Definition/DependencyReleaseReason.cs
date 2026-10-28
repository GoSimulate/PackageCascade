namespace PackageCascade.Public.Planning.Definition;

/// <summary>A release caused by a direct package dependency being released.</summary>
/// <param name="PackageId">The released dependency's package ID.</param>
/// <param name="MinimumBump">The minimum bump required for the dependant.</param>
public sealed record DependencyReleaseReason(string PackageId, SemverBump MinimumBump);
