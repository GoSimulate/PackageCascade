using LibGit2Sharp;
using PackageCascade.Public.Definition;

namespace PackageCascade.Internal.Executing.Git;

internal sealed class PackageTagWriter : IPackageTagWriter
{
	public IReadOnlyList<string> Create(string repositoryPath, ReleasePlan plan)
	{
		using Repository repository = new(repositoryPath);
		Commit target = repository.Head.Tip ?? throw new InvalidOperationException("The repository has no HEAD commit.");

		List<string> created = [];
		foreach (PlannedRelease release in plan.Packages)
		{
			string name = $"{release.PackageId}-v{release.Version}";
			repository.Tags.Add(name, target);
			created.Add(name);
		}

		return created;
	}
}
