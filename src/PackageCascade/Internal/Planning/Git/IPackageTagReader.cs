namespace PackageCascade.Internal.Planning.Git;

internal interface IPackageTagReader
{
	IReadOnlyDictionary<string, PackageReleaseTag> ReadLatest(string repositoryPath, IEnumerable<string> packageIds);
}
