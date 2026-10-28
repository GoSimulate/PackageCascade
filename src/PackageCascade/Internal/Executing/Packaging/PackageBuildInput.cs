namespace PackageCascade.Internal.Executing.Packaging;

internal sealed record PackageBuildInput(
	string SolutionPath,
	IReadOnlyList<string> ProjectPaths,
	IReadOnlyDictionary<string, string> VersionsByProjectPath,
	string Configuration,
	bool IncludeSymbols,
	string OutputDirectory);
