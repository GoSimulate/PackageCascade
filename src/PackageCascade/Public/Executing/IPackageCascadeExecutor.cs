using PackageCascade.Public.Planning.Definition;

namespace PackageCascade.Public.Executing;

/// <summary>Builds and packs the releases in a prepared plan, verifies the archives, then creates local Git tags.</summary>
public interface IPackageCascadeExecutor
{
	/// <summary>Applies the supplied plan at its target commit without publishing packages or pushing tags.</summary>
	/// <param name="plan">The prepared releases, target commit and build configuration.</param>
	/// <param name="repositoryPath">Path to the Git repository containing the solution.</param>
	/// <param name="solutionPath">Path to the solution to build.</param>
	/// <param name="options">Package output and optional symbol settings.</param>
	/// <param name="cancellationToken">Cancels SDK commands and subsequent execution steps.</param>
	/// <returns>The verified package archives and tags created by this invocation.</returns>
	public Task<ExecutionResult> Execute(
		ReleasePlan plan,
		string repositoryPath,
		string solutionPath,
		ExecutionOptions options,
		CancellationToken cancellationToken = default);
}
