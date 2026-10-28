using LibGit2Sharp;
using PackageCascade.Public.Planning.Definition;

namespace PackageCascade.Internal.Executing.Git;

internal sealed class PackageTagWriter : IPackageTagWriter
{
	public IReadOnlyList<string> Create(string repositoryPath, ReleasePlan plan)
	{
		using Repository repository = new(repositoryPath);
		Commit target = repository.Lookup<Commit>(plan.TargetCommitSha)
			?? throw new InvalidOperationException($"Target commit '{plan.TargetCommitSha}' was not found.");

		List<string> pending = [];
		foreach (PlannedRelease release in plan.Packages)
		{
			string name = $"{release.PackageId}-v{release.Version}";
			if (!Reference.IsValidName($"refs/tags/{name}"))
			{
				throw new InvalidOperationException($"Invalid package tag name '{name}'.");
			}

			Tag[] matchingTags = repository.Tags
				.Where(tag => MatchesRelease(tag.FriendlyName, release))
				.ToArray();
			if (matchingTags.Length == 0)
			{
				pending.Add(name);
			}
			foreach (Tag existing in matchingTags)
			{
				if (existing.PeeledTarget is not Commit commit || commit.Id != target.Id)
				{
					throw new InvalidOperationException($"Package tag '{existing.FriendlyName}' already points to a different target.");
				}
			}
		}

		List<string> created = [];
		try
		{
			foreach (string name in pending)
			{
				repository.Tags.Add(name, target);
				created.Add(name);
			}
		}
		catch (Exception createError)
		{
			try
			{
				Remove(repository, created);
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Package tag creation and rollback both failed.", createError, cleanupError);
			}

			throw;
		}

		return created;
	}

	private static bool MatchesRelease(string tagName, PlannedRelease release)
	{
		string versionSuffix = $"-v{release.Version}";
		return tagName.EndsWith(versionSuffix, StringComparison.Ordinal)
			&& tagName[..^versionSuffix.Length].Equals(release.PackageId, StringComparison.OrdinalIgnoreCase);
	}

	public void Remove(string repositoryPath, IReadOnlyList<string> tagNames)
	{
		using Repository repository = new(repositoryPath);
		Remove(repository, tagNames);
	}

	private static void Remove(Repository repository, IReadOnlyList<string> tagNames)
	{
		foreach (string name in tagNames)
		{
			repository.Tags.Remove(name);
		}
	}
}
