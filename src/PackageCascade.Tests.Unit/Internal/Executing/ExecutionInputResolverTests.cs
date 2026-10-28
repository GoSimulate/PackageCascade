using NuGet.Versioning;
using PackageCascade.Internal.Executing;
using PackageCascade.Internal.Planning.Git;
using PackageCascade.Internal.Planning.Projects;
using PackageCascade.Public.Planning.Definition;

namespace PackageCascade.Tests.Unit.Internal.Executing;

public sealed class ExecutionInputResolverTests
{
	private static readonly string _repositoryPath = Path.Combine(Path.GetTempPath(), "packagecascade-execution-input-tests");
	private static readonly string _solutionPath = Path.Combine(_repositoryPath, "Packages.slnx");
	private static readonly string _outputDirectory = Path.Combine(_repositoryPath, "artifacts");

	[Test]
	public async Task ResolvesTransitiveUnchangedDependenciesWithoutSelectingThemForPacking()
	{
		ResolvedExecutionInput input = Resolve(CreateResolver(), Release("A", "1.1.0"));

		using (Assert.Multiple())
		{
			await Assert.That(input.BuildInput.ProjectPaths).IsEquivalentTo(new[] { ProjectPath("A") });
			await Assert.That(input.BuildInput.VersionsByProjectPath.Keys).IsEquivalentTo(new[] { ProjectPath("A"), ProjectPath("B"), ProjectPath("C") });
			await Assert.That(input.BuildInput.VersionsByProjectPath[ProjectPath("A")]).IsEqualTo("1.1.0");
			await Assert.That(input.BuildInput.VersionsByProjectPath[ProjectPath("B")]).IsEqualTo("2.0.0");
			await Assert.That(input.BuildInput.VersionsByProjectPath[ProjectPath("C")]).IsEqualTo("3.0.0");
			await Assert.That(input.DirectDependencyVersionsByPackageId.Keys).IsEquivalentTo(new[] { "A" });
			await Assert.That(input.DirectDependencyVersionsByPackageId["A"].Keys).IsEquivalentTo(new[] { "B" });
			await Assert.That(input.DirectDependencyVersionsByPackageId["A"]["B"]).IsEqualTo("2.0.0");
		}
	}

	[Test]
	public void RejectsUnchangedDependencyWithoutReleaseTag()
	{
		Dictionary<string, PackageReleaseTag> tags = Tags();
		tags.Remove("C");

		Assert.Throws(typeof(InvalidOperationException), () => Resolve(CreateResolver(tags: new StubTagReader(tags)), Release("A", "1.1.0")));
	}

	[Test]
	public async Task SelectedDependencyVersionOverridesExistingTag()
	{
		ResolvedExecutionInput input = Resolve(CreateResolver(), Release("B", "2.1.0"), Release("A", "1.1.0"));

		using (Assert.Multiple())
		{
			await Assert.That(input.BuildInput.VersionsByProjectPath[ProjectPath("B")]).IsEqualTo("2.1.0");
			await Assert.That(input.DirectDependencyVersionsByPackageId["A"]["B"]).IsEqualTo("2.1.0");
			await Assert.That(input.DirectDependencyVersionsByPackageId["B"]["C"]).IsEqualTo("3.0.0");
		}
	}

	[Test]
	public async Task PreservesValidDependencyFirstPlanOrder()
	{
		ResolvedExecutionInput input = Resolve(CreateResolver(), Release("C", "3.1.0"), Release("B", "2.1.0"), Release("A", "1.1.0"));

		await Assert.That(input.BuildInput.ProjectPaths.SequenceEqual([ProjectPath("C"), ProjectPath("B"), ProjectPath("A")])).IsTrue();
	}

	[Test]
	public void RejectsSelectedDependantBeforeItsSelectedDependency()
	{
		Assert.Throws(typeof(InvalidOperationException), () => Resolve(CreateResolver(), Release("A", "1.1.0"), Release("B", "2.1.0")));
	}

	[Test]
	public void RejectsSelectedDependantBeforeItsTransitiveSelectedDependency()
	{
		Assert.Throws(typeof(InvalidOperationException), () => Resolve(CreateResolver(), Release("A", "1.1.0"), Release("C", "3.1.0")));
	}

	[Test]
	public void RejectsProjectPathAbsentFromDiscoveredSolution()
	{
		Assert.Throws(typeof(InvalidOperationException), () => Resolve(CreateResolver(), Release("Missing", "1.0.0")));
	}

	[Test]
	public void RejectsAbsolutePlannedProjectPath()
	{
		PlannedRelease release = Release("A", "1.1.0") with { ProjectPath = ProjectPath("A") };

		Assert.Throws(typeof(InvalidOperationException), () => Resolve(CreateResolver(), release));
	}

	[Test]
	public void RejectsPlannedProjectPathOutsideRepository()
	{
		PlannedRelease release = Release("A", "1.1.0") with { ProjectPath = "../A/A.csproj" };

		Assert.Throws(typeof(InvalidOperationException), () => Resolve(CreateResolver(), release));
	}

	[Test]
	public void RejectsPackageIdThatDoesNotMatchDiscoveredProject()
	{
		PlannedRelease release = Release("A", "1.1.0") with { PackageId = "Wrong.Id" };

		Assert.Throws(typeof(InvalidOperationException), () => Resolve(CreateResolver(), release));
	}

	[Test]
	public async Task MatchesPackageIdsWithoutCaseSensitivity()
	{
		PlannedRelease release = Release("A", "1.1.0") with { PackageId = "a" };

		ResolvedExecutionInput input = Resolve(CreateResolver(), release);

		using (Assert.Multiple())
		{
			await Assert.That(input.BuildInput.VersionsByProjectPath[ProjectPath("A")]).IsEqualTo("1.1.0");
			await Assert.That(input.DirectDependencyVersionsByPackageId["a"]["b"]).IsEqualTo("2.0.0");
		}
	}

	[Test]
	public void RejectsDuplicateSelectedProject()
	{
		Assert.Throws(typeof(InvalidOperationException), () => Resolve(CreateResolver(), Release("A", "1.1.0"), Release("A", "1.2.0")));
	}

	[Test]
	public void RejectsPackageIdsDifferingOnlyByCase()
	{
		PackableProject upper = Project("Upper") with { PackageId = "Acme" };
		PackableProject lower = Project("Lower") with { PackageId = "acme" };
		ExecutionInputResolver resolver = new(new StubDiscoverer([upper, lower]), new PackageDependencyGraphBuilder(),
			new StubTagReader(Tags()), new StubCommitReader("head-sha"));

		Assert.Throws(typeof(InvalidOperationException), () => Resolve(resolver,
			new PlannedRelease("Acme", "src/Upper/Upper.csproj", "1.0.0", []),
			new PlannedRelease("acme", "src/Lower/Lower.csproj", "1.0.0", [])));
	}

	[Test]
	public void RejectsInvalidSelectedVersion()
	{
		Assert.Throws(typeof(InvalidOperationException), () => Resolve(CreateResolver(), Release("A", "not-a-version")));
	}

	[Test]
	[Arguments("Debug")]
	[Arguments("Staging")]
	public async Task ResolvesPlannedProjectRelativeToRepositoryAndPassesExecutionOptions(string configuration)
	{
		StubDiscoverer discoverer = new(Graph());
		ExecutionInputResolver resolver = CreateResolver(discoverer: discoverer);
		ReleasePlan plan = new([Release("A", "1.1.0")], "head-sha", configuration);

		ResolvedExecutionInput input = resolver.Resolve(plan, _repositoryPath, "Packages.slnx", "artifacts", true);

		using (Assert.Multiple())
		{
			await Assert.That(discoverer.SolutionPath).IsEqualTo("Packages.slnx");
			await Assert.That(discoverer.Configuration).IsEqualTo(configuration);
			await Assert.That(input.BuildInput.SolutionPath).IsEqualTo("Packages.slnx");
			await Assert.That(input.BuildInput.OutputDirectory).IsEqualTo("artifacts");
			await Assert.That(input.BuildInput.ProjectPaths.Single()).IsEqualTo(ProjectPath("A"));
			await Assert.That(input.BuildInput.Configuration).IsEqualTo(configuration);
			await Assert.That(input.BuildInput.IncludeSymbols).IsTrue();
		}
	}

	[Test]
	public void RejectsHeadMismatch()
	{
		ExecutionInputResolver resolver = CreateResolver(commits: new StubCommitReader("different-head"));

		Assert.Throws(typeof(InvalidOperationException), () => Resolve(resolver, Release("A", "1.1.0")));
	}

	[Test]
	public async Task EmptyPlanProducesNoSelectedProjectsOrDependencyVersions()
	{
		StubTagReader tags = new(Tags());
		ResolvedExecutionInput input = Resolve(CreateResolver(tags: tags));

		using (Assert.Multiple())
		{
			await Assert.That(input.BuildInput.ProjectPaths).IsEmpty();
			await Assert.That(input.BuildInput.VersionsByProjectPath).IsEmpty();
			await Assert.That(input.DirectDependencyVersionsByPackageId).IsEmpty();
			await Assert.That(tags.WasCalled).IsFalse();
		}
	}

	private static ResolvedExecutionInput Resolve(ExecutionInputResolver resolver, params PlannedRelease[] releases)
		=> resolver.Resolve(new ReleasePlan(releases, "head-sha", "Release"), _repositoryPath, _solutionPath, _outputDirectory, false);

	private static ExecutionInputResolver CreateResolver(StubDiscoverer? discoverer = null, StubTagReader? tags = null, StubCommitReader? commits = null)
		=> new(discoverer ?? new StubDiscoverer(Graph()), new StubGraphBuilder(), tags ?? new StubTagReader(Tags()), commits ?? new StubCommitReader("head-sha"));

	private static PlannedRelease Release(string packageId, string version)
		=> new(packageId, $"src/{packageId}/{packageId}.csproj", version, []);

	private static string ProjectPath(string packageId) => Path.Combine(_repositoryPath, "src", packageId, packageId + ".csproj");
	private static PackableProject Project(string packageId, params string[] dependencies)
		=> new(ProjectPath(packageId), packageId, dependencies.Select(ProjectPath).ToArray());

	private static IReadOnlyList<PackableProject> Graph() => [Project("A", "B"), Project("B", "C"), Project("C"), Project("D")];

	private static Dictionary<string, PackageReleaseTag> Tags() => new()
	{
		["A"] = new("A-v1.0.0", NuGetVersion.Parse("1.0.0"), "release-a"),
		["B"] = new("B-v2.0.0", NuGetVersion.Parse("2.0.0"), "release-b"),
		["C"] = new("C-v3.0.0", NuGetVersion.Parse("3.0.0"), "release-c"),
	};

	private sealed class StubDiscoverer : ISolutionPackageDiscoverer
	{
		private readonly IReadOnlyList<PackableProject> _projects;

		public StubDiscoverer(IReadOnlyList<PackableProject> projects) => _projects = projects;

		public string? SolutionPath { get; private set; }
		public string? Configuration { get; private set; }

		public IReadOnlyList<PackableProject> Discover(string solutionPath, string configuration = "Release")
		{
			SolutionPath = solutionPath;
			Configuration = configuration;
			return _projects;
		}
	}

	private sealed class StubGraphBuilder : IPackageDependencyGraphBuilder
	{
		public IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> Build(IReadOnlyList<PackableProject> projects)
		{
			Dictionary<string, PackableProject> byId = projects.ToDictionary(project => project.PackageId, StringComparer.Ordinal);
			return new Dictionary<PackableProject, IReadOnlyList<PackableProject>>
			{
				[byId["A"]] = [byId["B"]],
				[byId["B"]] = [byId["C"]],
				[byId["C"]] = [],
				[byId["D"]] = [],
			};
		}
	}

	private sealed class StubTagReader : IPackageTagReader
	{
		private readonly IReadOnlyDictionary<string, PackageReleaseTag> _tags;

		public StubTagReader(IReadOnlyDictionary<string, PackageReleaseTag> tags) => _tags = tags;

		public bool WasCalled { get; private set; }

		public IReadOnlyDictionary<string, PackageReleaseTag> ReadLatest(string repositoryPath, IEnumerable<string> packageIds)
		{
			WasCalled = true;
			return packageIds.Where(_tags.ContainsKey).ToDictionary(id => id, id => _tags[id], StringComparer.Ordinal);
		}
	}

	private sealed class StubCommitReader : IGitCommitReader
	{
		private readonly string _headSha;

		public StubCommitReader(string headSha) => _headSha = headSha;

		public string ReadHeadSha(string repositoryPath) => _headSha;
		public IReadOnlyList<GitCommitDetails> ReadSince(string repositoryPath, string baselineCommitHash)
			=> throw new NotSupportedException("Execution input resolution must not read commit history.");
	}
}
