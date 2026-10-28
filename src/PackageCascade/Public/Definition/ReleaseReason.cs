namespace PackageCascade.Public.Definition;

/// <summary>Identifies why a package is included in a release plan.</summary>
public union ReleaseReason(ConventionalCommitReason, DependencyReleaseReason, InitialReleaseReason);
