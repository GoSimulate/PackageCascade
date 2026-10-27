using LibGit2Sharp;
using NuGet.Versioning;

namespace PackageCascade.Internal.Planning;

internal sealed class PackageTagReader
{
	public IReadOnlyDictionary<string, PackageReleaseTag> ReadLatest(string repositoryPath, IEnumerable<string> packageIds)
	{
		using Repository repository = new(repositoryPath);
		Commit head = repository.Head.Tip ?? throw new InvalidOperationException("The repository has no HEAD commit.");
		string[] distinctPackageIds = packageIds.Distinct(StringComparer.Ordinal).ToArray();
		Dictionary<string, List<PackageReleaseTag>> tagsByPackage = distinctPackageIds
			.ToDictionary(packageId => packageId, _ => new List<PackageReleaseTag>(), StringComparer.Ordinal);

		foreach (Tag tag in repository.Tags)
		{
			foreach (string packageId in distinctPackageIds)
			{
				string prefix = packageId + "-v";
				if (!tag.FriendlyName.StartsWith(prefix, StringComparison.Ordinal)
					|| !NuGetVersion.TryParseStrict(tag.FriendlyName[prefix.Length..], out NuGetVersion? version))
				{
					continue;
				}
				if (tag.PeeledTarget is not Commit commit || repository.ObjectDatabase.FindMergeBase(head, commit)?.Id != commit.Id)
				{
					continue;
				}

				tagsByPackage[packageId].Add(new(tag.FriendlyName, version, commit.Sha));
			}
		}

		Dictionary<string, PackageReleaseTag> latestTags = new(StringComparer.Ordinal);
		foreach ((string packageId, List<PackageReleaseTag> tags) in tagsByPackage)
		{
			if (tags.Count == 0)
			{
				continue;
			}

			PackageReleaseTag latest = tags.MaxBy(tag => tag.Version, VersionComparer.VersionRelease)!;
			foreach (PackageReleaseTag candidate in tags)
			{
				if (VersionComparer.VersionRelease.Compare(candidate.Version, latest.Version) == 0)
				{
					if (latest.CommitSha != candidate.CommitSha)
					{
						throw new InvalidOperationException($"Package '{packageId}' has equivalent version tags on different commits.");
					}

					if (string.CompareOrdinal(candidate.Name, latest.Name) < 0)
					{
						latest = candidate;
					}
				}
			}
			latestTags.Add(packageId, latest);
		}

		return latestTags;
	}
}
