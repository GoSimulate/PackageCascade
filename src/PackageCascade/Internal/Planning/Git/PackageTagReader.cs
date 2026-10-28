using LibGit2Sharp;
using NuGet.Versioning;

namespace PackageCascade.Internal.Planning.Git;

internal sealed class PackageTagReader : IPackageTagReader
{
	public IReadOnlyDictionary<string, PackageReleaseTag> ReadLatest(string repositoryPath, IEnumerable<string> packageIds)
	{
		using Repository repository = new(repositoryPath);
		Commit head = repository.Head.Tip ?? throw new InvalidOperationException("The repository has no HEAD commit.");
		Dictionary<string, List<PackageReleaseTag>> tagsByPackage = GetReachableTags(repository, head, packageIds);

		return GetLatestTags(tagsByPackage);
	}

	private static Dictionary<string, List<PackageReleaseTag>> GetReachableTags(
		Repository repository,
		Commit head,
		IEnumerable<string> packageIds)
	{
		Dictionary<string, List<PackageReleaseTag>> tagsByPackage = packageIds.Distinct(StringComparer.Ordinal)
			.ToDictionary(packageId => packageId, _ => new List<PackageReleaseTag>(), StringComparer.Ordinal);

		foreach (Tag tag in repository.Tags)
		{
			AddReachableMatches(repository, head, tagsByPackage, tag);
		}

		return tagsByPackage;
	}

	private static void AddReachableMatches(
		Repository repository,
		Commit head,
		IReadOnlyDictionary<string, List<PackageReleaseTag>> tagsByPackage,
		Tag tag)
	{
		foreach ((string packageId, List<PackageReleaseTag> tags) in tagsByPackage)
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

			tags.Add(new(tag.FriendlyName, version, commit.Sha));
		}
	}

	private static Dictionary<string, PackageReleaseTag> GetLatestTags(
		IReadOnlyDictionary<string, List<PackageReleaseTag>> tagsByPackage)
	{
		Dictionary<string, PackageReleaseTag> latestTags = new(StringComparer.Ordinal);

		foreach ((string packageId, List<PackageReleaseTag> tags) in tagsByPackage)
		{
			if (tags.Count > 0)
			{
				latestTags.Add(packageId, GetLatestTag(packageId, tags));
			}
		}

		return latestTags;
	}

	private static PackageReleaseTag GetLatestTag(string packageId, IReadOnlyList<PackageReleaseTag> tags)
	{
		PackageReleaseTag latest = tags.MaxBy(tag => tag.Version, VersionComparer.VersionRelease)!;

		foreach (PackageReleaseTag candidate in tags)
		{
			if (VersionComparer.VersionRelease.Compare(candidate.Version, latest.Version) != 0)
			{
				continue;
			}

			if (latest.CommitSha != candidate.CommitSha)
			{
				throw new InvalidOperationException($"Package '{packageId}' has equivalent version tags on different commits.");
			}

			if (string.CompareOrdinal(candidate.Name, latest.Name) < 0)
			{
				latest = candidate;
			}
		}

		return latest;
	}
}
