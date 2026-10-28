namespace PackageCascade.Internal.Planning.Git;

internal sealed record GitCommitDetails(string Sha, string Message, IReadOnlyList<string> ChangedPaths);
