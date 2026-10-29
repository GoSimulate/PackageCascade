using LibGit2Sharp;

namespace PackageCascade.Internal.Planning.Git;

internal sealed class GitCommitReader : IGitCommitReader
{
	public IReadOnlyList<GitCommitDetails> ReadSince(string repositoryPath, string baselineCommitHash, string solutionDirectory)
	{
		IReadOnlyList<string> globalJsonPaths = GetGlobalJsonPaths(solutionDirectory);
		using Repository repository = new(repositoryPath);
		Commit head = repository.Head.Tip ?? throw new InvalidOperationException("The repository has no HEAD commit.");
		Commit baseline = repository.Lookup<Commit>(baselineCommitHash)
			?? throw new ArgumentException("The baseline commit was not found.", nameof(baselineCommitHash));

		if (repository.ObjectDatabase.FindMergeBase(head, baseline)?.Id != baseline.Id)
		{
			throw new ArgumentException("The baseline commit is not an ancestor of HEAD.", nameof(baselineCommitHash));
		}

		CommitFilter filter = new()
		{
			IncludeReachableFrom = head,
			ExcludeReachableFrom = baseline,
			SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse,
		};

		List<GitCommitDetails> commits = new();

		foreach (Commit commit in repository.Commits.QueryBy(filter))
		{
			Commit parent = commit.Parents.First();
			IReadOnlyList<string> changedPaths = GetChangedPaths(repository, parent, commit);
			string? previousGlobalJson = FindGlobalJson(parent.Tree, globalJsonPaths);
			string? currentGlobalJson = FindGlobalJson(commit.Tree, globalJsonPaths);
			commits.Add(new(commit.Sha, commit.Message.Trim(), changedPaths)
			{
				ChangesEffectiveGlobalJson = (previousGlobalJson is not null && changedPaths.Contains(previousGlobalJson))
					|| (currentGlobalJson is not null && changedPaths.Contains(currentGlobalJson)),
			});
		}

		return commits;
	}

	private static IReadOnlyList<string> GetGlobalJsonPaths(string solutionDirectory)
	{
		if (solutionDirectory == ".." || solutionDirectory.StartsWith("../", StringComparison.Ordinal))
		{
			return [];
		}

		List<string> paths = [];
		string directory = solutionDirectory is "." or "" ? "" : solutionDirectory.TrimEnd('/');
		while (true)
		{
			paths.Add(directory.Length == 0 ? "global.json" : directory + "/global.json");
			if (directory.Length == 0)
			{
				return paths;
			}

			int separator = directory.LastIndexOf('/');
			directory = separator < 0 ? "" : directory[..separator];
		}
	}

	private static string? FindGlobalJson(Tree tree, IEnumerable<string> paths)
	{
		return paths.FirstOrDefault(path => tree[path]?.Target is Blob);
	}

	private static IReadOnlyList<string> GetChangedPaths(Repository repository, Commit parent, Commit commit)
	{
		TreeChanges changes = repository.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
		HashSet<string> changedPaths = new(StringComparer.Ordinal);

		foreach (TreeEntryChanges change in changes)
		{
			if (!string.IsNullOrEmpty(change.Path))
			{
				changedPaths.Add(change.Path);
			}

			if (!string.IsNullOrEmpty(change.OldPath))
			{
				changedPaths.Add(change.OldPath);
			}
		}

		return changedPaths.Order(StringComparer.Ordinal).ToArray();
	}
}
