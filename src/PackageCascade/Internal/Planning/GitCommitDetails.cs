namespace PackageCascade.Internal.Planning;

internal sealed record GitCommitDetails(string Sha, string Message, IReadOnlyList<string> ChangedPaths);
