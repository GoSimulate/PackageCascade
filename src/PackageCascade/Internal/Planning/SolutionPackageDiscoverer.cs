using System.Collections.Immutable;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;
using Microsoft.Build.Locator;

namespace PackageCascade.Internal.Planning;

internal sealed class SolutionPackageDiscoverer
{
	static SolutionPackageDiscoverer()
	{
		if (!MSBuildLocator.IsRegistered)
		{
			MSBuildLocator.RegisterDefaults();
		}
	}

	public IReadOnlyList<PackableProject> Discover(string solutionPath)
	{
		ProjectGraph graph = new(Path.GetFullPath(solutionPath));
		StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		HashSet<string> packablePaths = graph.EntryPointNodes
			.Where(IsPackable)
			.Select(node => node.ProjectInstance.FullPath)
			.ToHashSet(pathComparer);
		Dictionary<string, ProjectGraphNode[]> projects = graph.ProjectNodes
			.Where(node => packablePaths.Contains(node.ProjectInstance.FullPath))
			.GroupBy(node => node.ProjectInstance.FullPath, pathComparer)
			.ToDictionary(group => group.Key, group => group.ToArray(), pathComparer);

		return projects
			.Select(project => CreatePackage(project.Key, project.Value, projects, pathComparer))
			.OrderBy(package => package.ProjectPath, StringComparer.Ordinal)
			.ToImmutableArray();
	}

	private static bool IsPackable(ProjectGraphNode node)
	{
		return bool.TryParse(node.ProjectInstance.GetPropertyValue("IsPackable"), out bool isPackable) && isPackable;
	}

	private static PackableProject CreatePackage(
		string projectPath,
		IReadOnlyList<ProjectGraphNode> nodes,
		IReadOnlyDictionary<string, ProjectGraphNode[]> projects,
		StringComparer pathComparer)
	{
		string packageId = nodes[0].ProjectInstance.GetPropertyValue("PackageId");
		HashSet<string> referencedPaths = new(pathComparer);

		foreach (ProjectGraphNode node in nodes)
		{
			string currentPackageId = node.ProjectInstance.GetPropertyValue("PackageId");
			if (string.IsNullOrWhiteSpace(currentPackageId))
			{
				throw new InvalidOperationException($"Packable project '{projectPath}' has no PackageId.");
			}

			if (packageId != currentPackageId)
			{
				throw new InvalidOperationException($"Project '{projectPath}' has different PackageIds across evaluations.");
			}

			foreach (ProjectItemInstance reference in node.ProjectInstance.GetItems("ProjectReference"))
			{
				string referencedPath = reference.GetMetadataValue("FullPath");
				if (projects.ContainsKey(referencedPath))
				{
					referencedPaths.Add(referencedPath);
				}
			}
		}

		return new(projectPath, packageId, referencedPaths.OrderBy(path => path, StringComparer.Ordinal).ToImmutableArray());
	}
}
