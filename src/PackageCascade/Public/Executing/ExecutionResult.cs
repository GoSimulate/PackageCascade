namespace PackageCascade.Public.Executing;

/// <summary>Local artefacts and new tags produced by applying a release plan.</summary>
/// <param name="PackagePaths">Verified package and optional symbol archive paths.</param>
/// <param name="CreatedTags">Names of local tags created by this invocation.</param>
public sealed record ExecutionResult(IReadOnlyList<string> PackagePaths, IReadOnlyList<string> CreatedTags);
