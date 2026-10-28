namespace PackageCascade.Internal.Planning.Git;

internal interface IPackageTagReader
{
	public IReadOnlyDictionary<string, PackageReleaseTag> ReadLatest(string repositoryPath, IEnumerable<string> packageIds);
}
