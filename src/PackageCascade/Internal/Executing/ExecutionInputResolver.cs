using NuGet.Versioning;
using PackageCascade.Internal.Executing.Packaging;
using PackageCascade.Internal.Planning.Git;
using PackageCascade.Internal.Planning.Projects;
using PackageCascade.Public.Planning.Definition;

namespace PackageCascade.Internal.Executing;

internal sealed class ExecutionInputResolver : IExecutionInputResolver
{
	private readonly ISolutionPackageDiscoverer _discoverer;
	private readonly IPackageDependencyGraphBuilder _graphBuilder;
	private readonly IPackageTagReader _tagReader;
	private readonly IGitCommitReader _commitReader;

	public ExecutionInputResolver(
		ISolutionPackageDiscoverer discoverer,
		IPackageDependencyGraphBuilder graphBuilder,
		IPackageTagReader tagReader,
		IGitCommitReader commitReader)
	{
		_discoverer = discoverer;
		_graphBuilder = graphBuilder;
		_tagReader = tagReader;
		_commitReader = commitReader;
	}

	public ResolvedExecutionInput Resolve(
		ReleasePlan plan,
		string repositoryPath,
		string solutionPath,
		string outputDirectory,
		bool includeSymbols)
	{
		string repositoryRoot = Path.GetFullPath(repositoryPath);
		if (!_commitReader.ReadHeadSha(repositoryRoot).Equals(plan.TargetCommitSha, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("The release plan targets a different HEAD commit.");
		}

		if (plan.Packages.Count == 0)
		{
			return new(new(solutionPath, [], new Dictionary<string, string>(), plan.Configuration, includeSymbols, outputDirectory),
				new Dictionary<string, IReadOnlyDictionary<string, string>>());
		}

		IReadOnlyList<PackableProject> projects = _discoverer.Discover(solutionPath, plan.Configuration);
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> graph = _graphBuilder.Build(projects);
		StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		Dictionary<string, PackableProject> projectsByPath = projects.ToDictionary(project => Path.GetFullPath(project.ProjectPath), pathComparer);
		List<PackableProject> selected = ResolveSelectedProjects(plan.Packages, repositoryRoot, projectsByPath);

		HashSet<PackableProject> selectedSet = selected.ToHashSet();
		ValidateSelectedOrder(selected, graph);
		HashSet<PackableProject> closure = GetDependencyClosure(selected, graph);
		Dictionary<string, string> versionsByPath = ResolveVersions(
			plan.Packages, selected, closure, selectedSet, repositoryRoot, pathComparer);

		Dictionary<string, IReadOnlyDictionary<string, string>> directDependencies = selected.ToDictionary(
			project => project.PackageId,
			project => (IReadOnlyDictionary<string, string>)graph[project].ToDictionary(
				dependency => dependency.PackageId,
				dependency => versionsByPath[Path.GetFullPath(dependency.ProjectPath)],
				StringComparer.OrdinalIgnoreCase),
			StringComparer.OrdinalIgnoreCase);

		PackageBuildInput buildInput = new(solutionPath,
			selected.Select(project => Path.GetFullPath(project.ProjectPath)).ToArray(),
			versionsByPath, plan.Configuration, includeSymbols, outputDirectory);
		return new(buildInput, directDependencies);
	}

	private static List<PackableProject> ResolveSelectedProjects(
		IReadOnlyList<PlannedRelease> releases,
		string repositoryRoot,
		IReadOnlyDictionary<string, PackableProject> projectsByPath)
	{
		List<PackableProject> selected = [];
		HashSet<string> packageIds = new(StringComparer.OrdinalIgnoreCase);

		foreach (PlannedRelease release in releases)
		{
			string projectPath = ResolveProjectPath(repositoryRoot, release.ProjectPath);
			if (!projectsByPath.TryGetValue(projectPath, out PackableProject? project)
				|| !project.PackageId.Equals(release.PackageId, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"Planned package '{release.PackageId}' does not match a packable solution project at '{release.ProjectPath}'.");
			}

			if (!packageIds.Add(release.PackageId))
			{
				throw new InvalidOperationException($"Planned package '{release.PackageId}' is duplicated.");
			}

			if (!NuGetVersion.TryParseStrict(release.Version, out _))
			{
				throw new InvalidOperationException($"Planned package '{release.PackageId}' has an invalid version '{release.Version}'.");
			}

			selected.Add(project);
		}

		return selected;
	}

	private static string ResolveProjectPath(string repositoryRoot, string projectPath)
	{
		if (Path.IsPathRooted(projectPath))
		{
			throw new InvalidOperationException($"Planned project path '{projectPath}' must be relative to the repository.");
		}

		string fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, projectPath));
		string relativePath = Path.GetRelativePath(repositoryRoot, fullPath);
		if (relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
		{
			throw new InvalidOperationException($"Planned project path '{projectPath}' escapes the repository.");
		}

		return fullPath;
	}

	private static HashSet<PackableProject> GetDependencyClosure(
		IEnumerable<PackableProject> selected,
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> graph)
	{
		HashSet<PackableProject> closure = [];
		Stack<PackableProject> pending = new(selected);

		while (pending.TryPop(out PackableProject? project))
		{
			if (!closure.Add(project))
			{
				continue;
			}

			foreach (PackableProject dependency in graph[project])
			{
				pending.Push(dependency);
			}
		}

		return closure;
	}

	private Dictionary<string, string> ResolveVersions(
		IReadOnlyList<PlannedRelease> releases,
		IReadOnlyList<PackableProject> selected,
		IReadOnlySet<PackableProject> closure,
		IReadOnlySet<PackableProject> selectedSet,
		string repositoryRoot,
		StringComparer pathComparer)
	{
		Dictionary<string, string> versionsByPath = new(pathComparer);
		for (int index = 0; index < selected.Count; index++)
		{
			versionsByPath.Add(Path.GetFullPath(selected[index].ProjectPath), releases[index].Version);
		}

		PackableProject[] unchanged = closure.Except(selectedSet)
			.OrderBy(project => project.PackageId, StringComparer.Ordinal)
			.ToArray();
		if (unchanged.Length == 0)
		{
			return versionsByPath;
		}

		IReadOnlyDictionary<string, PackageReleaseTag> tags = _tagReader.ReadLatest(repositoryRoot,
			unchanged.Select(project => project.PackageId));
		foreach (PackableProject project in unchanged)
		{
			if (!tags.TryGetValue(project.PackageId, out PackageReleaseTag? tag))
			{
				throw new InvalidOperationException($"Dependency '{project.PackageId}' has no reachable release tag.");
			}

			versionsByPath.Add(Path.GetFullPath(project.ProjectPath), tag.Version.ToNormalizedString());
		}

		return versionsByPath;
	}

	private static void ValidateSelectedOrder(
		IReadOnlyList<PackableProject> selected,
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> graph)
	{
		Dictionary<PackableProject, int> position = selected
			.Select((project, index) => (project, index))
			.ToDictionary(entry => entry.project, entry => entry.index);

		foreach (PackableProject project in selected)
		{
			if (GetDependencyClosure(graph[project], graph).Any(dependency =>
				position.TryGetValue(dependency, out int dependencyPosition) && dependencyPosition >= position[project]))
			{
				throw new InvalidOperationException($"Planned package '{project.PackageId}' appears before one of its selected dependencies.");
			}
		}
	}
}
