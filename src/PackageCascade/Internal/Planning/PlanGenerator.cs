using PackageCascade.Internal.Planning.Git;
using PackageCascade.Internal.Planning.Projects;
using PackageCascade.Internal.Planning.SemVer;
using NuGet.Versioning;
using PackageCascade.Public.Planning;

namespace PackageCascade.Internal.Planning;

internal sealed class PlanGenerator : IPackageCascadePlanner
{
	private readonly ISolutionPackageDiscoverer _packageDiscoverer;
	private readonly IGitCommitReader _commitReader;
	private readonly IPackageTagReader _tagReader;
	private readonly IPackageDependencyGraphBuilder _graphBuilder;
	private readonly IDirectoryBumpCalculator _directoryCalculator;
	private readonly IPackageBumpCalculator _bumpCalculator;

	public PlanGenerator(
		ISolutionPackageDiscoverer packageDiscoverer,
		IGitCommitReader commitReader,
		IPackageTagReader tagReader,
		IPackageDependencyGraphBuilder graphBuilder,
		IDirectoryBumpCalculator directoryCalculator,
		IPackageBumpCalculator bumpCalculator)
	{
		_packageDiscoverer = packageDiscoverer;
		_commitReader = commitReader;
		_tagReader = tagReader;
		_graphBuilder = graphBuilder;
		_directoryCalculator = directoryCalculator;
		_bumpCalculator = bumpCalculator;
	}

	public ReleasePlan Generate(
		string repositoryPath,
		string solutionPath)
	{
		IReadOnlyList<PackableProject> projects = _packageDiscoverer.Discover(solutionPath);
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> graph = _graphBuilder.Build(projects);
		IReadOnlyDictionary<string, PackageReleaseTag> tags = _tagReader.ReadLatest(repositoryPath, projects.Select(project => project.PackageId));
		Dictionary<PackableProject, DirectChange> directChanges = GetDirectChanges(repositoryPath, projects, tags);
		Dictionary<string, SemverBump> directoryBumps = directChanges.ToDictionary(entry => GetDirectory(repositoryPath, entry.Key), entry => entry.Value.Bump, StringComparer.Ordinal);
		IReadOnlyDictionary<PackableProject, SemverBump> bumps = _bumpCalculator.Calculate(repositoryPath, graph, directoryBumps);
		HashSet<PackableProject> selected = bumps.Where(entry => entry.Value != SemverBump.None).Select(entry => entry.Key).ToHashSet();
		Dictionary<PackableProject, NuGetVersion> versions = GetVersions(selected, tags, bumps);
		IReadOnlyList<IReadOnlyList<string>> layers = GetBuildLayers(selected, graph);
		IReadOnlyDictionary<string, NuGetVersion> effectiveVersions = GetEffectiveVersions(selected, graph, tags, versions);
		IReadOnlyList<PlannedRelease> packages = CreatePackages(
			repositoryPath, layers, selected, graph, tags, directChanges, versions, effectiveVersions);
		return new(effectiveVersions, packages, layers);
	}

	private Dictionary<PackableProject, DirectChange> GetDirectChanges(
		string repositoryPath,
		IEnumerable<PackableProject> projects,
		IReadOnlyDictionary<string, PackageReleaseTag> tags)
	{
		Dictionary<PackableProject, DirectChange> changes = new();
		Dictionary<string, IReadOnlyList<GitCommitDetails>> commitsByBaseline = new(StringComparer.Ordinal);
		foreach (PackableProject project in projects)
		{
			if (!tags.TryGetValue(project.PackageId, out PackageReleaseTag? tag))
			{
				changes.Add(project, new(SemverBump.Patch, []));
				continue;
			}

			if (!commitsByBaseline.TryGetValue(tag.CommitSha, out IReadOnlyList<GitCommitDetails>? commits))
			{
				commits = _commitReader.ReadSince(repositoryPath, tag.CommitSha);
				commitsByBaseline.Add(tag.CommitSha, commits);
			}

			changes.Add(project, GetDirectChange(commits, GetDirectory(repositoryPath, project)));
		}
		return changes;
	}

	private DirectChange GetDirectChange(IEnumerable<GitCommitDetails> commits, string directory)
	{
		SemverBump strongestBump = SemverBump.None;
		List<string> commitShas = [];
		foreach (GitCommitDetails commit in commits)
		{
			SemverBump bump = _directoryCalculator.Calculate([commit], [directory])[directory];
			if (bump == SemverBump.None)
			{
				continue;
			}

			strongestBump = (SemverBump)Math.Max((int)strongestBump, (int)bump);
			commitShas.Add(commit.Sha);
		}
		return new(strongestBump, commitShas.Order(StringComparer.Ordinal).ToArray());
	}

	private static Dictionary<PackableProject, NuGetVersion> GetVersions(
		IEnumerable<PackableProject> selected,
		IReadOnlyDictionary<string, PackageReleaseTag> tags,
		IReadOnlyDictionary<PackableProject, SemverBump> bumps)
	{
		Dictionary<PackableProject, NuGetVersion> versions = new();
		foreach (PackableProject project in selected)
		{
			NuGetVersion version = tags.TryGetValue(project.PackageId, out PackageReleaseTag? tag)
				? BumpVersion(tag.Version, bumps[project])
				: new(0, 1, 0);
			versions.Add(project, version);
		}
		return versions;
	}

	private static IReadOnlyDictionary<string, NuGetVersion> GetEffectiveVersions(
		IEnumerable<PackableProject> selected,
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> graph,
		IReadOnlyDictionary<string, PackageReleaseTag> tags,
		IReadOnlyDictionary<PackableProject, NuGetVersion> versions)
	{
		HashSet<PackableProject> required = new(selected);
		Queue<PackableProject> pending = new(required);
		while (pending.TryDequeue(out PackableProject? project))
		{
			foreach (PackableProject dependency in graph[project])
			{
				if (required.Add(dependency))
				{
					pending.Enqueue(dependency);
				}
			}
		}

		Dictionary<string, NuGetVersion> effectiveVersions = new(StringComparer.Ordinal);
		foreach (PackableProject project in required.OrderBy(project => project.PackageId, StringComparer.Ordinal))
		{
			NuGetVersion version = versions.TryGetValue(project, out NuGetVersion? plannedVersion)
				? plannedVersion
				: tags[project.PackageId].Version;
			effectiveVersions.Add(project.PackageId, version);
		}
		return effectiveVersions;
	}

	private static IReadOnlyList<IReadOnlyList<string>> GetBuildLayers(
		IEnumerable<PackableProject> selected,
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> graph)
	{
		HashSet<PackableProject> remaining = new(selected);
		List<IReadOnlyList<string>> layers = [];
		while (remaining.Count > 0)
		{
			PackableProject[] ready = remaining
				.Where(project => graph[project].All(dependency => !remaining.Contains(dependency)))
				.OrderBy(project => project.PackageId, StringComparer.Ordinal)
				.ToArray();
			if (ready.Length == 0)
			{
				throw new InvalidOperationException("The package dependency graph contains a cycle.");
			}

			layers.Add(ready.Select(project => project.PackageId).ToArray());
			remaining.ExceptWith(ready);
		}
		return layers;
	}

	private static IReadOnlyList<PlannedRelease> CreatePackages(
		string repositoryPath,
		IEnumerable<IReadOnlyList<string>> layers,
		IReadOnlySet<PackableProject> selected,
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> graph,
		IReadOnlyDictionary<string, PackageReleaseTag> tags,
		IReadOnlyDictionary<PackableProject, DirectChange> directChanges,
		IReadOnlyDictionary<PackableProject, NuGetVersion> versions,
		IReadOnlyDictionary<string, NuGetVersion> effectiveVersions)
	{
		Dictionary<string, PackableProject> projectsById = selected.ToDictionary(project => project.PackageId, StringComparer.Ordinal);
		List<PlannedRelease> packages = [];
		foreach (string packageId in layers.SelectMany(layer => layer))
		{
			PackableProject project = projectsById[packageId];
			tags.TryGetValue(packageId, out PackageReleaseTag? previousTag);
			NuGetVersion version = versions[project];
			packages.Add(new(
				packageId,
				Path.GetRelativePath(repositoryPath, project.ProjectPath).Replace('\\', '/'),
				previousTag?.Version,
				version,
				previousTag?.Name,
				packageId + "-v" + version.ToNormalizedString(),
				GetReasons(project, previousTag, directChanges[project], selected, graph),
				graph[project]
					.OrderBy(dependency => dependency.PackageId, StringComparer.Ordinal)
					.Select(dependency => new PlannedDependency(
						dependency.PackageId, new(effectiveVersions[dependency.PackageId], true)))
					.ToArray()));
		}
		return packages;
	}

	private static IReadOnlyList<ReleaseReason> GetReasons(
		PackableProject project,
		PackageReleaseTag? previousTag,
		DirectChange directChange,
		IReadOnlySet<PackableProject> selected,
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> graph)
	{
		List<ReleaseReason> reasons = [];
		if (previousTag is null)
		{
			reasons.Add(new InitialReleaseReason());
		}
		else if (directChange.Bump != SemverBump.None)
		{
			reasons.Add(new ConventionalCommitReason(ReleaseScope.Project, directChange.Bump, directChange.CommitShas));
		}

		foreach (PackableProject dependency in graph[project].Where(selected.Contains).OrderBy(dependency => dependency.PackageId, StringComparer.Ordinal))
		{
			reasons.Add(new DependencyReleaseReason(dependency.PackageId, SemverBump.Patch));
		}
		return reasons;
	}

	private static string GetDirectory(string repositoryPath, PackableProject project)
	{
		return Path.GetRelativePath(repositoryPath, Path.GetDirectoryName(project.ProjectPath)!).Replace('\\', '/');
	}

	private static NuGetVersion BumpVersion(NuGetVersion version, SemverBump bump)
	{
		return bump switch
		{
			SemverBump.Patch => new(version.Major, version.Minor, version.Patch + 1),
			SemverBump.Minor => new(version.Major, version.Minor + 1, 0),
			SemverBump.Major => new(version.Major + 1, 0, 0),
			_ => throw new ArgumentOutOfRangeException(nameof(bump)),
		};
	}

	private sealed record DirectChange(SemverBump Bump, IReadOnlyList<string> CommitShas);
}
