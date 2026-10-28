using PackageCascade.Public.Planning.Definition;

namespace PackageCascade.Internal.Executing.Git;

internal interface IPackageTagWriter
{
	public IReadOnlyList<string> Create(string repositoryPath, ReleasePlan plan);
}
