namespace PackageCascade.Internal.Planning.Git;

internal interface IGitCommitReader
{
	IReadOnlyList<GitCommitDetails> ReadSince(string repositoryPath, string baselineCommitHash);
}
