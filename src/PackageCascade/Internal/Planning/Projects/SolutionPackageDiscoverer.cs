using System.Collections.Immutable;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;
using Microsoft.Build.Locator;

namespace PackageCascade.Internal.Planning.Projects;

internal sealed class SolutionPackageDiscoverer : ISolutionPackageDiscoverer
{
	static SolutionPackageDiscoverer()
	{
		if (!MSBuildLocator.IsRegistered)
		{
			MSBuildLocator.RegisterDefaults();
		}
	}

	public IReadOnlyList<PackableProject> Discover(string solutionPath, string configuration = "Release")
	{
		ProjectGraph graph = new(Path.GetFullPath(solutionPath), new Dictionary<string, string> { ["Configuration"] = configuration });
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
		HashSet<string> packageDependencyPaths = new(pathComparer);

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

			CollectReferences(node, projects, referencedPaths, packageDependencyPaths);
		}

		return new(projectPath, packageId, referencedPaths.OrderBy(path => path, StringComparer.Ordinal).ToImmutableArray())
		{
			PackageDependencyProjectPaths = packageDependencyPaths.OrderBy(path => path, StringComparer.Ordinal).ToImmutableArray(),
		};
	}

	private static void CollectReferences(
		ProjectGraphNode node,
		IReadOnlyDictionary<string, ProjectGraphNode[]> projects,
		HashSet<string> referencedPaths,
		HashSet<string> packageDependencyPaths)
	{
		foreach (ProjectItemInstance reference in node.ProjectInstance.GetItems("ProjectReference"))
		{
			string referencedPath = reference.GetMetadataValue("FullPath");
			if (!projects.ContainsKey(referencedPath))
			{
				continue;
			}

			referencedPaths.Add(referencedPath);
			if (!IsPrivateReference(reference))
			{
				packageDependencyPaths.Add(referencedPath);
			}
		}
	}

	private static bool IsPrivateReference(ProjectItemInstance reference)
	{
		return reference.GetMetadataValue("PrivateAssets")
			.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
			.Contains("all", StringComparer.OrdinalIgnoreCase);
	}
}
