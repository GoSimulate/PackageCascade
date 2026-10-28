using PackageCascade.Internal.Planning.Projects;
using PackageCascade.Public.Planning;
namespace PackageCascade.Internal.Planning.SemVer;

internal interface IPackageBumpCalculator
{
	IReadOnlyDictionary<PackableProject, SemverBump> Calculate(
		string repositoryPath,
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> dependencyGraph,
		IReadOnlyDictionary<string, SemverBump> directoryBumps);
}
