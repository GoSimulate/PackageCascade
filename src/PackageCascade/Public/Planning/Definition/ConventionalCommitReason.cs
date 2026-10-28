namespace PackageCascade.Public.Planning.Definition;

/// <summary>A release caused by conventional commits relevant to the package.</summary>
/// <param name="Scope">Where the relevant changes were found.</param>
/// <param name="Bump">The strongest version bump required by those commits.</param>
/// <param name="CommitShas">Identifiers of the commits that contributed to the bump.</param>
public sealed record ConventionalCommitReason(
	ReleaseScope Scope,
	SemverBump Bump,
	IReadOnlyList<string> CommitShas);
