namespace PackageCascade.Internal.Executing.Packaging;

internal sealed record ExpectedPackage(
	string PackageId,
	string Version,
	IReadOnlyDictionary<string, string> DirectDependencyVersions);
