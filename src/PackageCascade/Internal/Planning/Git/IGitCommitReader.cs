namespace PackageCascade.Internal.Planning.Git;

internal interface IGitCommitReader
{
	public string ReadHeadSha(string repositoryPath);

	public IReadOnlyList<GitCommitDetails> ReadSince(string repositoryPath, string baselineCommitHash);
}
