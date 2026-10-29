namespace PackageCascade.Internal.Planning.Git;

internal interface IGitCommitReader
{
	public IReadOnlyList<GitCommitDetails> ReadSince(string repositoryPath, string baselineCommitHash, string solutionDirectory);
}
