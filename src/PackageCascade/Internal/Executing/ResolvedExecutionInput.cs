using PackageCascade.Internal.Executing.Packaging;

namespace PackageCascade.Internal.Executing;

internal sealed record ResolvedExecutionInput(
	PackageBuildInput BuildInput,
	IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DirectDependencyVersionsByPackageId);
