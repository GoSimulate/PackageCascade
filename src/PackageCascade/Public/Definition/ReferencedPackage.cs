namespace PackageCascade.Public.Definition;

/// <summary>An unchanged packable project required to build a selected release.</summary>
/// <param name="PackageId">The package's evaluated ID.</param>
/// <param name="ProjectPath">The project file path; relative paths are resolved from the repository root.</param>
/// <param name="Version">The package's latest reachable released version as a normalised string.</param>
public sealed record ReferencedPackage(string PackageId, string ProjectPath, string Version);
