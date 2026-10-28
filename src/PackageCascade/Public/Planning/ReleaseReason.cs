namespace PackageCascade.Public.Planning;

/// <summary>Identifies why a package is included in a release plan.</summary>
public union ReleaseReason(ConventionalCommitReason, DependencyReleaseReason, InitialReleaseReason);
