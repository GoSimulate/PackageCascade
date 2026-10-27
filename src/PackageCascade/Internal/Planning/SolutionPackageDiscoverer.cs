using System.Collections.Immutable;
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
		Dictionary<string, PackableProject> packages = new(pathComparer);

		foreach (ProjectGraphNode node in graph.EntryPointNodes)
		{
			if (!bool.TryParse(node.ProjectInstance.GetPropertyValue("IsPackable"), out bool isPackable) || !isPackable)
			{
				continue;
			}

			string projectPath = node.ProjectInstance.FullPath;
			string packageId = node.ProjectInstance.GetPropertyValue("PackageId");
			if (string.IsNullOrWhiteSpace(packageId))
			{
				throw new InvalidOperationException($"Packable project '{projectPath}' has no PackageId.");
			}

			if (packages.TryGetValue(projectPath, out PackableProject? existing) && existing.PackageId != packageId)
			{
				throw new InvalidOperationException($"Project '{projectPath}' has different PackageIds across evaluations.");
			}

			packages[projectPath] = new(projectPath, packageId);
		}

		return packages.Values.OrderBy(package => package.ProjectPath, StringComparer.Ordinal).ToImmutableArray();
	}
}
