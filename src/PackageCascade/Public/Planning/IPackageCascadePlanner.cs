using PackageCascade.Public.Definition;

namespace PackageCascade.Public.Planning;

/// <summary>Plans package releases from a solution's packable projects and Git history.</summary>
public interface IPackageCascadePlanner
{
	/// <summary>Calculates releases using each package's latest reachable tag as its baseline and repository HEAD as the endpoint.</summary>
	/// <param name="repositoryPath">Path to the Git repository containing the projects.</param>
	/// <param name="solutionPath">Path to the solution whose packable projects should be planned.</param>
	/// <param name="configuration">MSBuild configuration used to discover packable projects and dependencies. Defaults to <c>Release</c>.</param>
	/// <returns>The selected releases in dependency-first order.</returns>
	public ReleasePlan Generate(string repositoryPath, string solutionPath, string configuration = "Release");
}
