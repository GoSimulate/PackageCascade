using PackageCascade.Internal.Planning.Projects;
using PackageCascade.Public.Definition;
namespace PackageCascade.Internal.Planning.SemVer;

internal interface IPackageBumpCalculator
{
	public IReadOnlyDictionary<PackableProject, SemverBump> Calculate(
		string repositoryPath,
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> dependencyGraph,
		IReadOnlyDictionary<string, SemverBump> directoryBumps);
}
