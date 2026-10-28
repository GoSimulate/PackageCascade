using LibGit2Sharp;
using PackageCascade.Public.Definition;

namespace PackageCascade.Internal.Executing.Git;

internal sealed class PackageTagWriter : IPackageTagWriter
{
	public IReadOnlyList<string> Create(string repositoryPath, ReleasePlan plan)
	{
		using Repository repository = new(repositoryPath);
		Commit target = repository.Lookup<Commit>(plan.TargetCommitSha)
			?? throw new InvalidOperationException($"Target commit '{plan.TargetCommitSha}' was not found.");

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
