using PackageCascade.Public.Planning.Definition;

namespace PackageCascade.Internal.Executing;

internal interface IExecutionInputResolver
{
	public ResolvedExecutionInput Resolve(
		ReleasePlan plan,
		string repositoryPath,
		string solutionPath,
		string outputDirectory,
		bool includeSymbols);
}
