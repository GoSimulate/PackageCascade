using PackageCascade.Internal.Planning.Projects;
using PackageCascade.Public.Planning;
using System.Diagnostics.Contracts;

namespace PackageCascade.Internal.Planning.SemVer;

internal sealed class PackageBumpCalculator : IPackageBumpCalculator
{
	[Pure]
	public IReadOnlyDictionary<PackableProject, SemverBump> Calculate(
		string repositoryPath,
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> dependencyGraph,
		IReadOnlyDictionary<string, SemverBump> directoryBumps)
	{
		Dictionary<PackableProject, SemverBump> bumps = GetDirectBumps(repositoryPath, dependencyGraph.Keys, directoryBumps);
		Dictionary<PackableProject, List<PackableProject>> dependants = GetDependants(dependencyGraph);
		return PropagateDependencyBumps(bumps, dependants);
	}

	[Pure]
	private static Dictionary<PackableProject, SemverBump> GetDirectBumps(
		string repositoryPath,
		IEnumerable<PackableProject> projects,
		IReadOnlyDictionary<string, SemverBump> directoryBumps)
	{
		Dictionary<PackableProject, SemverBump> bumps = new();
		foreach (PackableProject project in projects)
		{
			string directory = Path.GetRelativePath(repositoryPath, Path.GetDirectoryName(project.ProjectPath)!)
				.Replace('\\', '/');
			bumps.Add(project, directoryBumps[directory]);
		}
		return bumps;
	}

	[Pure]
	private static Dictionary<PackableProject, List<PackableProject>> GetDependants(
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> dependencyGraph)
	{
		Dictionary<PackableProject, List<PackableProject>> dependants = dependencyGraph.Keys
			.ToDictionary(project => project, _ => new List<PackableProject>());
		foreach ((PackableProject project, IReadOnlyList<PackableProject> dependencies) in dependencyGraph)
		{
			foreach (PackableProject dependency in dependencies)
			{
				dependants[dependency].Add(project);
			}
		}
		return dependants;
	}

	[Pure]
	private static Dictionary<PackableProject, SemverBump> PropagateDependencyBumps(
		IReadOnlyDictionary<PackableProject, SemverBump> directBumps,
		IReadOnlyDictionary<PackableProject, List<PackableProject>> dependants)
	{
		Dictionary<PackableProject, SemverBump> bumps = new(directBumps);
		Queue<PackableProject> bumpedProjects = new(bumps
			.Where(entry => entry.Value != SemverBump.None)
			.Select(entry => entry.Key));
		while (bumpedProjects.Count > 0)
		{
			PackableProject bumpedProject = bumpedProjects.Dequeue();
			foreach (PackableProject dependant in dependants[bumpedProject])
			{
				if (bumps[dependant] == SemverBump.None)
				{
					bumps[dependant] = SemverBump.Patch;
					bumpedProjects.Enqueue(dependant);
				}
			}
		}
		return bumps;
	}
}
