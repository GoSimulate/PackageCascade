using LibGit2Sharp;

namespace PackageCascade.Internal.Planning;

internal sealed class GitCommitReader
{
	public IReadOnlyList<GitCommitDetails> ReadSince(string repositoryPath, string baselineCommitHash)
	{
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

			commits.Add(new GitCommitDetails(commit.Sha, commit.Message.Trim(),
				changedPaths.Order(StringComparer.Ordinal).ToArray()));
		}

		return commits;
	}
}
