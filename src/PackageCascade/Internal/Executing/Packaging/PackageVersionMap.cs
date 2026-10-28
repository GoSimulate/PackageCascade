namespace PackageCascade.Internal.Executing.Packaging;

internal sealed record PackageVersionMap(
	string ExecutionId,
	IReadOnlyDictionary<string, string> VersionsByTargetPath);
