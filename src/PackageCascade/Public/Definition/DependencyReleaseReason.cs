namespace PackageCascade.Public.Definition;

/// <summary>A release caused by a direct package dependency being released.</summary>
/// <param name="PackageId">The released dependency's package ID.</param>
public sealed record DependencyReleaseReason(string PackageId);
