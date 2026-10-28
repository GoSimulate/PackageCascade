namespace PackageCascade.Internal.Planning.Projects;

internal sealed class PackageDependencyGraphBuilder : IPackageDependencyGraphBuilder
{
	public IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> Build(IReadOnlyList<PackableProject> projects)
	{
		StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		Dictionary<string, PackableProject> projectsByPath = projects.ToDictionary(project => Path.GetFullPath(project.ProjectPath), pathComparer);
		Dictionary<PackableProject, IReadOnlyList<PackableProject>> dependencies = new();

		foreach (PackableProject project in projects)
		{
			HashSet<PackableProject> directDependencies = new();
			foreach (string referencedPath in project.ReferencedProjectPaths)
			{
				if (projectsByPath.TryGetValue(Path.GetFullPath(referencedPath), out PackableProject? dependency))
				{
					directDependencies.Add(dependency);
				}
			}

			dependencies.Add(project, directDependencies.OrderBy(dependency => dependency.ProjectPath, StringComparer.Ordinal).ToArray());
		}

		return dependencies;
	}
}
