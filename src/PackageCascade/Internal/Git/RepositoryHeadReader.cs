using LibGit2Sharp;

namespace PackageCascade.Internal.Git;

internal sealed class RepositoryHeadReader : IRepositoryHeadReader
{
	public string ReadHeadSha(string repositoryPath)
	{
		using Repository repository = new(repositoryPath);
		return repository.Head.Tip?.Sha ?? throw new InvalidOperationException("The repository has no HEAD commit.");
	}
}
