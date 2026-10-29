using PackageCascade.Internal.Planning.Git;
using PackageCascade.Internal.Planning.Projects;
using PackageCascade.Internal.Planning.SemVer;
using PackageCascade.Public.Planning;
using PackageCascade.Public.Definition;
using System.Text.Json;
using NuGet.Versioning;
using PackageCascade.Internal.Planning;

namespace PackageCascade.Tests.Unit.Internal.Planning;

public sealed class PlanGeneratorTests
{
	private static readonly string _repositoryPath = Path.Combine(Path.GetTempPath(), "packagecascade-planner-unit-tests");
	private static readonly string _solutionPath = Path.Combine(_repositoryPath, "Packages.slnx");

	[Test]
	public async Task ReleasesEveryUntaggedPackageAtInitialVersionWithoutCommits()
	{
		ReleasePlan plan = Generate(Graph(), new Dictionary<string, PackageReleaseTag>());

		using (Assert.Multiple())
		{
			await Assert.That(plan.Packages.Select(package => package.PackageId)).IsEquivalentTo(new[] { "A", "B", "C", "D", "Unrelated" });

			foreach (PlannedRelease package in plan.Packages)
			{
				await Assert.That(package.Version).IsEqualTo("0.1.0");
				await Assert.That(package.Reasons.Count(reason => reason is InitialReleaseReason)).IsEqualTo(1);
			}

			await Assert.That(plan.ReferencedPackages).IsEmpty();
		}
	}

	[Test]
	public async Task InitialReleaseStaysAtInitialVersionEvenWithBreakingCommit()
	{
		ReleasePlan plan = Generate([Project("New")], new Dictionary<string, PackageReleaseTag>(),
			[Commit("breaking", "feat!: introduce API", "src/New/Api.cs")]);

		using (Assert.Multiple())
		{
			await Assert.That(plan.Packages.Single().Version).IsEqualTo("0.1.0");
			await Assert.That(plan.Packages.Single().Reasons.Count(reason => reason is InitialReleaseReason)).IsEqualTo(1);
		}
	}

	[Test]
	public async Task InitialDependencyReleasePatchesAlreadyReleasedDependants()
	{
		Dictionary<string, PackageReleaseTag> tags = Tags();
		tags.Remove("C");

		ReleasePlan plan = Generate(Graph(), tags);

		using (Assert.Multiple())
		{
			await AssertVersions(plan, ("A", "1.2.4"), ("B", "2.3.5"), ("C", "0.1.0"), ("D", "4.5.7"));
			await Assert.That(Release(plan, "C").Reasons.Count(reason => reason is InitialReleaseReason)).IsEqualTo(1);
			await AssertDependencyReason(plan, "B", "C");
		}
	}

	[Test]
	public async Task FoundationFixReleasesAllTransitiveDependantsWithIndependentVersions()
	{
		ReleasePlan plan = Generate(Graph(), Tags(), [Commit("fix-c", "fix: repair C", "src/C/Api.cs")]);

		using (Assert.Multiple())
		{
			await AssertVersions(plan, ("A", "1.2.4"), ("B", "2.3.5"), ("C", "3.4.6"), ("D", "4.5.7"));
			await AssertCommitReason(plan, "C", SemverBump.Patch, "fix-c");
			await AssertDependencyReason(plan, "A", "B");
			await AssertDependencyReason(plan, "B", "C");
			await AssertDependencyReason(plan, "D", "C");
			await Assert.That(string.Join(",", plan.Packages.Select(package => package.PackageId))).IsEqualTo("C,B,D,A");
		}
	}

	[Test]
	public async Task LibraryFeatureReleasesOnlyLibraryAndItsDependants()
	{
		ReleasePlan plan = Generate(Graph(), Tags(), [Commit("feat-b", "feat: extend B", "src/B/Api.cs")]);

		using (Assert.Multiple())
		{
			await AssertVersions(plan, ("A", "1.2.4"), ("B", "2.4.0"));
			await AssertCommitReason(plan, "B", SemverBump.Minor, "feat-b");
			await AssertDependencyReason(plan, "A", "B");
			await Assert.That(string.Join(",", plan.Packages.Select(package => package.PackageId))).IsEqualTo("B,A");
			await Assert.That(Release(plan, "A").DependencyIds).IsEquivalentTo(["B"]);
			await Assert.That(Release(plan, "B").DependencyIds).IsEquivalentTo(["C"]);
			await Assert.That(plan.ReferencedPackages.Single()).IsEqualTo(new ReferencedPackage("C", "src/C/C.csproj", "3.4.5"));
		}
	}

	[Test]
	public async Task PrivateReferenceStillCascadesAndOrdersReleasesWithoutPackageDependency()
	{
		PackableProject application = Project("A", "B") with { PackageDependencyProjectPaths = [] };
		ReleasePlan plan = Generate([application, Project("B", "C"), Project("C")], Tags(),
			[Commit("fix-b", "fix: repair B", "src/B/Api.cs")]);

		using (Assert.Multiple())
		{
			await AssertVersions(plan, ("A", "1.2.4"), ("B", "2.3.5"));
			await AssertDependencyReason(plan, "A", "B");
			await Assert.That(string.Join(",", plan.Packages.Select(package => package.PackageId))).IsEqualTo("B,A");
			await Assert.That(Release(plan, "A").DependencyIds).IsEmpty();
			await Assert.That(Release(plan, "B").DependencyIds).IsEquivalentTo(["C"]);
			await Assert.That(plan.ReferencedPackages.Single()).IsEqualTo(new ReferencedPackage("C", "src/C/C.csproj", "3.4.5"));
		}
	}

	[Test]
	public async Task ApplicationFixDoesNotReleaseItsDependenciesOrUnrelatedPackages()
	{
		ReleasePlan plan = Generate(Graph(), Tags(), [Commit("fix-a", "fix: repair A", "src/A/Api.cs")]);

		using (Assert.Multiple())
		{
			await AssertVersions(plan, ("A", "1.2.4"));
			await Assert.That(plan.Packages.Single().PackageId).IsEqualTo("A");
			await Assert.That(plan.Packages.Single().DependencyIds).IsEquivalentTo(["B"]);
			await Assert.That(plan.ReferencedPackages.Select(package => package.PackageId)).IsEquivalentTo(["B", "C"]);
			await Assert.That(plan.ReferencedPackages.Single(package => package.PackageId == "B").Version).IsEqualTo("2.3.4");
		}
	}

	[Test]
	public async Task ReferencedPackageVersionUsesCaseInsensitiveTagIdentity()
	{
		Dictionary<string, PackageReleaseTag> tags = new(StringComparer.OrdinalIgnoreCase)
		{
			["a"] = Tag("a", "1.2.3", "release-a"),
			["b"] = Tag("b", "2.3.4", "release-b"),
		};

		ReleasePlan plan = Generate([Project("A", "B"), Project("B")], tags,
			[Commit("fix-a", "fix: repair A", "src/A/Api.cs")]);

		using (Assert.Multiple())
		{
			await Assert.That(plan.Packages.Single().PackageId).IsEqualTo("A");
			await Assert.That(plan.Packages.Single().DependencyIds).IsEquivalentTo(["B"]);
			await Assert.That(plan.ReferencedPackages.Single().Version).IsEqualTo("2.3.4");
		}
	}

	[Test]
	public async Task BreakingDependencyChangeOnlyPatchesDependantsWithoutTheirOwnChanges()
	{
		ReleasePlan plan = Generate(Graph(), Tags(), [Commit("break-c", "feat!: replace C API", "src/C/Api.cs")]);

		using (Assert.Multiple())
		{
			await AssertVersions(plan, ("A", "1.2.4"), ("B", "2.3.5"), ("C", "4.0.0"), ("D", "4.5.7"));
			await AssertCommitReason(plan, "C", SemverBump.Major, "break-c");
		}
	}

	[Test]
	public async Task DirectMinorBumpWinsOverDependencyPatchAndRetainsBothReasons()
	{
		ReleasePlan plan = Generate(Graph(), Tags(),
			[Commit("feat-b", "feat: extend B", "src/B/Api.cs"), Commit("fix-c", "fix: repair C", "src/C/Api.cs")]);

		using (Assert.Multiple())
		{
			await AssertVersions(plan, ("A", "1.2.4"), ("B", "2.4.0"), ("C", "3.4.6"), ("D", "4.5.7"));
			await AssertCommitReason(plan, "B", SemverBump.Minor, "feat-b");
			await AssertDependencyReason(plan, "B", "C");
		}
	}

	[Test]
	public async Task MultipleDirectCommitsUseStrongestBumpRegardlessOfCommitOrder()
	{
		GitCommitDetails[] commits =
		[
			Commit("fix-b", "fix: repair B", "src/B/Api.cs"),
			Commit("break-b", "fix!: replace B API", "src/B/Api.cs"),
			Commit("feat-b", "feat: extend B", "src/B/Other.cs"),
		];

		ReleasePlan first = Generate(Graph(), Tags(), commits);
		ReleasePlan reversed = Generate(Graph(), Tags(), commits.Reverse().ToArray());

		using (Assert.Multiple())
		{
			await AssertVersions(first, ("A", "1.2.4"), ("B", "3.0.0"));
			await AssertVersions(reversed, ("A", "1.2.4"), ("B", "3.0.0"));
			await AssertCommitReason(first, "B", SemverBump.Major, "break-b");
		}
	}

	[Test]
	public async Task CalculatesEachPackageFromItsOwnReleaseTagBaseline()
	{
		GitCommitDetails oldFeature = Commit("older-feature", "feat: extend both packages", "src/Older/Api.cs", "src/Newer/Api.cs");
		Dictionary<string, PackageReleaseTag> tags = new()
		{
			["Older"] = Tag("Older", "1.0.0", "older-release"),
			["Newer"] = Tag("Newer", "4.0.0", "newer-release"),
		};

		StubCommitReader commits = new(new Dictionary<string, IReadOnlyList<GitCommitDetails>>
		{
			["older-release"] = [oldFeature],
			["newer-release"] = [],
		});

		PlanGenerator generator = CreateGenerator([Project("Older"), Project("Newer")], commits, tags);

		ReleasePlan plan = generator.Generate(_repositoryPath, _solutionPath);

		using (Assert.Multiple())
		{
			await AssertVersions(plan, ("Older", "1.1.0"));
			await AssertCommitReason(plan, "Older", SemverBump.Minor, "older-feature");
		}
	}

	[Test]
	public async Task PlanContainsOnlyPackagesSelectedForRelease()
	{
		ReleasePlan plan = Generate(Graph(), Tags(), [Commit("feat-b", "feat: extend B", "src/B/Api.cs")]);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(",", plan.Packages.Select(package => package.PackageId))).IsEqualTo("B,A");
			await Assert.That(Release(plan, "A").Version).IsEqualTo("1.2.4");
			await Assert.That(Release(plan, "B").Version).IsEqualTo("2.4.0");
		}
	}

	[Test]
	public async Task RecordsPackageIdentityProjectPathAndTargetVersion()
	{
		ReleasePlan plan = Generate(Graph(), Tags(), [Commit("fix-c", "fix: repair C", "src/C/Api.cs")]);
		PlannedRelease package = Release(plan, "C");

		using (Assert.Multiple())
		{
			await Assert.That(package.ProjectPath.Replace('\\', '/')).IsEqualTo("src/C/C.csproj");
			await Assert.That(package.Version).IsEqualTo("3.4.6");
		}
	}

	[Test]
	public async Task TestOnlyAndReleaseWorkflowOnlyCommitsDoNotReleaseTaggedPackages()
	{
		ReleasePlan plan = Generate(Graph(), Tags(),
			[Commit("test-feature", "feat: add test coverage", "tests/C.Tests/Tests.cs"),
			 Commit("workflow-fix", "fix: repair publishing workflow", ".github/workflows/release.yml")]);

		using (Assert.Multiple())
		{
			await Assert.That(plan.Packages).IsEmpty();
			await Assert.That(plan.ReferencedPackages).IsEmpty();
		}
	}

	[Test]
	public async Task NonBumpingProjectCommitsDoNotReleaseTaggedPackages()
	{
		ReleasePlan plan = Generate(Graph(), Tags(), [Commit("docs-c", "docs: explain C", "src/C/README.md")]);

		await Assert.That(plan.Packages).IsEmpty();
	}

	[Test]
	public async Task EmptyPlanHasNoPackages()
	{
		ReleasePlan plan = Generate(Graph(), Tags());

		await Assert.That(plan.Packages).IsEmpty();
	}

	[Test]
	public async Task SameInputsProduceIdenticalSerialisedPlans()
	{
		GitCommitDetails[] commits = [Commit("fix-c", "fix: repair C", "src/C/Api.cs")];

		ReleasePlan first = Generate(Graph(), Tags(), commits);
		ReleasePlan second = Generate(Graph(), Tags(), commits);

		await Assert.That(JsonSerializer.Serialize(first)).IsEqualTo(JsonSerializer.Serialize(second));
	}

	[Test]
	[Arguments("Debug")]
	[Arguments("Staging")]
	public async Task IncludesRequestedConfiguration(string configuration)
	{
		StubCommitReader commits = new([]);
		StubDiscoverer discoverer = new(Graph());
		PlanGenerator generator = CreateGenerator(discoverer, commits, new Dictionary<string, PackageReleaseTag>());

		ReleasePlan plan = generator.Generate(_repositoryPath, _solutionPath, configuration);

		using (Assert.Multiple())
		{
			await Assert.That(plan.Configuration).IsEqualTo(configuration);
			await Assert.That(discoverer.Configuration).IsEqualTo(configuration);
		}
	}

	[Test]
	public async Task RecordsSolutionDirectoryRelativeToRepository()
	{
		PlanGenerator generator = CreateGenerator(Graph(), new StubCommitReader([]), Tags());
		string solutionPath = Path.Combine(_repositoryPath, "solutions", "nested", "Packages.slnx");

		ReleasePlan plan = generator.Generate(_repositoryPath, solutionPath);

		await Assert.That(plan.WorkingDirectory).IsEqualTo("solutions/nested");
	}

	[Test]
	public async Task PassesSolutionDirectoryToBumpCalculator()
	{
		StubDirectoryBumpCalculator calculator = new();
		PlanGenerator generator = CreateGenerator(new StubDiscoverer(Graph()),
			new StubCommitReader([Commit("change", "fix: update settings", "solutions/Directory.Build.props")]),
			Tags(), calculator);

		generator.Generate(_repositoryPath, Path.Combine(_repositoryPath, "solutions", "Packages.slnx"));

		await Assert.That(calculator.SolutionDirectory).IsEqualTo("solutions");
	}

	private static ReleasePlan Generate(IReadOnlyList<PackableProject> projects,
		IReadOnlyDictionary<string, PackageReleaseTag> tags, IReadOnlyList<GitCommitDetails>? commits = null)
	{
		PlanGenerator generator = CreateGenerator(projects, new StubCommitReader(commits ?? []), tags);

		return generator.Generate(_repositoryPath, _solutionPath);
	}

	private static PlanGenerator CreateGenerator(IReadOnlyList<PackableProject> projects, IGitCommitReader commits,
		IReadOnlyDictionary<string, PackageReleaseTag> tags)
		=> CreateGenerator(new StubDiscoverer(projects), commits, tags);

	private static PlanGenerator CreateGenerator(StubDiscoverer discoverer, IGitCommitReader commits,
		IReadOnlyDictionary<string, PackageReleaseTag> tags, IDirectoryBumpCalculator? directoryCalculator = null)
		=> new(discoverer, commits, new StubTagReader(tags), new StubGraphBuilder(),
			directoryCalculator ?? new StubDirectoryBumpCalculator(), new StubPackageBumpCalculator());

	private static async Task AssertVersions(ReleasePlan plan, params (string PackageId, string Version)[] expected)
	{
		using (Assert.Multiple())
		{
			await Assert.That(plan.Packages.Select(package => package.PackageId)).IsEquivalentTo(expected.Select(package => package.PackageId));

			foreach ((string packageId, string version) in expected)
			{
				await Assert.That(Release(plan, packageId).Version).IsEqualTo(version);
			}
		}
	}

	private static async Task AssertCommitReason(ReleasePlan plan, string packageId, SemverBump bump, string sha)
	{
		await Assert.That(Release(plan, packageId).Reasons.Any(reason =>
			reason is ConventionalCommitReason commitReason
			&& commitReason.Bump == bump && commitReason.CommitShas.Contains(sha))).IsTrue();
	}

	private static async Task AssertDependencyReason(ReleasePlan plan, string packageId, string dependencyId)
	{
		await Assert.That(Release(plan, packageId).Reasons.Any(reason =>
			reason is DependencyReleaseReason dependencyReason && dependencyReason.PackageId == dependencyId)).IsTrue();
	}

	private static PlannedRelease Release(ReleasePlan plan, string packageId) => plan.Packages.Single(package => package.PackageId == packageId);
	private static NuGetVersion Version(string version) => NuGetVersion.Parse(version);
	private static string ProjectPath(string name) => Path.Combine(_repositoryPath, "src", name, name + ".csproj");
	private static PackableProject Project(string name, params string[] dependencies) => new(ProjectPath(name), name, dependencies.Select(ProjectPath).ToArray());
	private static IReadOnlyList<PackableProject> Graph() => [Project("A", "B"), Project("B", "C"), Project("C"), Project("D", "C"), Project("Unrelated")];
	private static GitCommitDetails Commit(string sha, string message, params string[] paths) => new(sha, message, paths);
	private static PackageReleaseTag Tag(string id, string version, string baseline) => new(id + "-v" + version, Version(version), baseline);
	private static Dictionary<string, PackageReleaseTag> Tags() => new()
	{
		["A"] = Tag("A", "1.2.3", "release-a"),
		["B"] = Tag("B", "2.3.4", "release-b"),
		["C"] = Tag("C", "3.4.5", "release-c"),
		["D"] = Tag("D", "4.5.6", "release-d"),
		["Unrelated"] = Tag("Unrelated", "5.0.0", "release-unrelated"),
	};

	private sealed class StubDiscoverer : ISolutionPackageDiscoverer
	{
		private readonly IReadOnlyList<PackableProject> _projects;
		public string? Configuration { get; private set; }

		public StubDiscoverer(IReadOnlyList<PackableProject> projects) => _projects = projects;

		public IReadOnlyList<PackableProject> Discover(string solutionPath, string configuration = "Release")
		{
			Configuration = configuration;
			return _projects;
		}
	}

	private sealed class StubTagReader : IPackageTagReader
	{
		private readonly IReadOnlyDictionary<string, PackageReleaseTag> _tags;

		public StubTagReader(IReadOnlyDictionary<string, PackageReleaseTag> tags) => _tags = tags;

		public IReadOnlyDictionary<string, PackageReleaseTag> ReadLatest(string repositoryPath, IEnumerable<string> packageIds) => _tags;
	}

	private sealed class StubCommitReader : IGitCommitReader
	{
		private readonly IReadOnlyList<GitCommitDetails> _commits = [];
		private readonly IReadOnlyDictionary<string, IReadOnlyList<GitCommitDetails>>? _commitsByBaseline;

		public StubCommitReader(IReadOnlyList<GitCommitDetails> commits) => _commits = commits;
		public StubCommitReader(IReadOnlyDictionary<string, IReadOnlyList<GitCommitDetails>> commitsByBaseline) => _commitsByBaseline = commitsByBaseline;

		public IReadOnlyList<GitCommitDetails> ReadSince(string repositoryPath, string baselineCommitHash)
			=> _commitsByBaseline is null ? _commits : _commitsByBaseline[baselineCommitHash];
	}

	private sealed class StubGraphBuilder : IPackageDependencyGraphBuilder
	{
		public IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> Build(IReadOnlyList<PackableProject> projects)
		{
			Dictionary<string, PackableProject> byPath = projects.ToDictionary(project => project.ProjectPath, StringComparer.Ordinal);

			return projects.ToDictionary(project => project,
				project => (IReadOnlyList<PackableProject>)project.ReferencedProjectPaths.Select(path => byPath[path]).ToArray());
		}
	}

	private sealed class StubDirectoryBumpCalculator : IDirectoryBumpCalculator
	{
		public string? SolutionDirectory { get; private set; }

		public Dictionary<string, SemverBump> Calculate(IEnumerable<GitCommitDetails> commits, IEnumerable<string> directories,
			string solutionDirectory)
		{
			SolutionDirectory = solutionDirectory;
			Dictionary<string, SemverBump> bumps = directories.ToDictionary(directory => directory,
				_ => SemverBump.None, StringComparer.Ordinal);

			foreach (GitCommitDetails commit in commits)
			{
				SemverBump bump = commit.Message.StartsWith("feat!:", StringComparison.Ordinal)
					|| commit.Message.StartsWith("fix!:", StringComparison.Ordinal) ? SemverBump.Major
					: commit.Message.StartsWith("feat:", StringComparison.Ordinal) ? SemverBump.Minor
					: commit.Message.StartsWith("fix:", StringComparison.Ordinal) ? SemverBump.Patch
					: SemverBump.None;

				foreach (string directory in bumps.Keys.ToArray())
				{
					if (commit.ChangedPaths.Any(path => path.StartsWith(directory + "/", StringComparison.Ordinal))
						&& bump > bumps[directory])
					{
						bumps[directory] = bump;
					}
				}
			}

			return bumps;
		}
	}

	private sealed class StubPackageBumpCalculator : IPackageBumpCalculator
	{
		public IReadOnlyDictionary<PackableProject, SemverBump> Calculate(string repositoryPath,
			IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> dependencyGraph,
			IReadOnlyDictionary<string, SemverBump> directoryBumps)
		{
			Dictionary<PackableProject, SemverBump> directBumps = dependencyGraph.Keys.ToDictionary(project => project,
				project => directoryBumps[Path.GetRelativePath(repositoryPath, Path.GetDirectoryName(project.ProjectPath)!).Replace('\\', '/')]);
			Dictionary<PackableProject, SemverBump> bumps = new();

			foreach (PackableProject project in dependencyGraph.Keys)
			{
				GetBump(project, dependencyGraph, directBumps, bumps);
			}

			return bumps;
		}

		private static SemverBump GetBump(PackableProject project,
			IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> graph,
			IReadOnlyDictionary<PackableProject, SemverBump> directBumps,
			Dictionary<PackableProject, SemverBump> bumps)
		{
			if (!bumps.TryGetValue(project, out SemverBump bump))
			{
				bump = directBumps[project];
				if (bump == SemverBump.None && graph[project].Any(dependency => GetBump(dependency, graph, directBumps, bumps) != SemverBump.None))
				{
					bump = SemverBump.Patch;
				}

				bumps.Add(project, bump);
			}

			return bump;
		}
	}
}
