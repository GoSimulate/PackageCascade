using System.Xml.Linq;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Public.Definition;
using PackageCascade.Public.DependencyInjection;
using PackageCascade.Public.Planning;
using PackageCascade.Tests.Integration.Fixtures;

namespace PackageCascade.Tests.Integration.CoreOnly.Planning;

public sealed class RepositoryPlanningIntegrationTests
{
	[Arguments("chore: update shared build properties", SemverBump.Patch, "1.2.4", "2.3.5", "3.4.6", "4.5.7")]
	[Arguments("feat!: change shared build contract", SemverBump.Major, "2.0.0", "3.0.0", "4.0.0", "5.0.0")]
	[Test]
	public async Task SharedRootChangeReleasesEveryPackageWithItsOwnBaseline(
		string message, SemverBump bump, string foundationVersion, string libraryVersion,
		string appVersion, string unrelatedVersion)
	{
		using RepositoryFixture fixture = new();
		fixture.AddProject("Foundation");
		fixture.AddProject("Library");
		fixture.AddProject("App");
		fixture.AddProject("Unrelated");
		fixture.AddReference("Library", "Foundation");
		fixture.AddReference("App", "Library");
		fixture.Commit("chore: add package graph", ".");
		fixture.Tag("Foundation", "1.2.3");
		fixture.Tag("Library", "2.3.4");
		fixture.Tag("App", "3.4.5");
		fixture.Tag("Unrelated", "4.5.6");

		fixture.Write("Directory.Build.props", "<Project><PropertyGroup><NuGetAudit>false</NuGetAudit><VersionPrefix>1.0.0</VersionPrefix></PropertyGroup></Project>");
		Commit changed = fixture.Commit(message, "Directory.Build.props");
		ReleasePlan plan = Generate(fixture);

		using (Assert.Multiple())
		{
			await AssertPlan(plan, "Release", ("Foundation", "Foundation"), ("Library", "Library"),
				("App", "App"), ("Unrelated", "Unrelated"));
			await PlanAssertions.HasReleases(plan,
				("Foundation", foundationVersion, []), ("Library", libraryVersion, ["Foundation"]),
				("App", appVersion, ["Library"]), ("Unrelated", unrelatedVersion, []));
			await PlanAssertions.HasReasons(plan, "Foundation", new ConventionalCommitReason(bump, [changed.Sha]));
			await PlanAssertions.HasReasons(plan, "Library", new ConventionalCommitReason(bump, [changed.Sha]),
				new DependencyReleaseReason("Foundation"));
			await PlanAssertions.HasReasons(plan, "App", new ConventionalCommitReason(bump, [changed.Sha]),
				new DependencyReleaseReason("Library"));
			await PlanAssertions.HasReasons(plan, "Unrelated", new ConventionalCommitReason(bump, [changed.Sha]));
			await PlanAssertions.Precedes(plan, "Foundation", "Library");
			await PlanAssertions.Precedes(plan, "Library", "App");
		}
	}

	[Test]
	public async Task SharedPatchAndLocalFeatureKeepTheirDistinctDirectReasons()
	{
		using RepositoryFixture fixture = new();
		fixture.AddProject("Foundation");
		fixture.AddProject("Library");
		fixture.AddProject("App");
		fixture.AddProject("Unrelated");
		fixture.AddReference("Library", "Foundation");
		fixture.AddReference("App", "Library");
		fixture.Commit("chore: add package graph", ".");
		fixture.Tag("Foundation", "1.2.3");
		fixture.Tag("Library", "2.3.4");
		fixture.Tag("App", "3.4.5");
		fixture.Tag("Unrelated", "4.5.6");

		fixture.Write("Directory.Build.props", "<Project><PropertyGroup><NuGetAudit>false</NuGetAudit><VersionPrefix>1.0.0</VersionPrefix></PropertyGroup></Project>");
		Commit shared = fixture.Commit("chore: update shared build properties", "Directory.Build.props");
		fixture.Write("src/Library/Api.cs", "public static class Library { public static int Value => 43; }\n");
		Commit feature = fixture.Commit("feat: extend library API", "src/Library/Api.cs");
		ReleasePlan plan = Generate(fixture);

		using (Assert.Multiple())
		{
			await AssertPlan(plan, "Release", ("Foundation", "Foundation"), ("Library", "Library"),
				("App", "App"), ("Unrelated", "Unrelated"));
			await PlanAssertions.HasReleases(plan,
				("Foundation", "1.2.4", []), ("Library", "2.4.0", ["Foundation"]),
				("App", "3.4.6", ["Library"]), ("Unrelated", "4.5.7", []));
			await PlanAssertions.HasReasons(plan, "Foundation", new ConventionalCommitReason(SemverBump.Patch, [shared.Sha]));
			await PlanAssertions.HasReasons(plan, "Library", new ConventionalCommitReason(SemverBump.Minor, [shared.Sha, feature.Sha]),
				new DependencyReleaseReason("Foundation"));
			await PlanAssertions.HasReasons(plan, "App", new ConventionalCommitReason(SemverBump.Patch, [shared.Sha]),
				new DependencyReleaseReason("Library"));
			await PlanAssertions.HasReasons(plan, "Unrelated", new ConventionalCommitReason(SemverBump.Patch, [shared.Sha]));
			await PlanAssertions.Precedes(plan, "Foundation", "Library");
			await PlanAssertions.Precedes(plan, "Library", "App");
		}
	}

	[Test]
	public async Task PrivateReferencePropagatesReleaseWithoutPackageDependency()
	{
		using RepositoryFixture fixture = new();
		fixture.AddProject("Foundation");
		fixture.AddProject("Library");
		fixture.AddReference("Library", "Foundation", privateAssets: true);
		fixture.Commit("chore: add package graph", ".");
		fixture.Tag("Foundation", "1.2.3");
		fixture.Tag("Library", "2.3.4");

		fixture.Write("src/Foundation/Api.cs", "public static class Foundation { public static int Value => 43; }\n");
		Commit feature = fixture.Commit("feat: extend foundation API", "src/Foundation/Api.cs");
		ReleasePlan plan = Generate(fixture);

		using (Assert.Multiple())
		{
			await AssertPlan(plan, "Release", ("Foundation", "Foundation"), ("Library", "Library"));
			await PlanAssertions.HasReleases(plan, ("Foundation", "1.3.0", []), ("Library", "2.3.5", []));
			await PlanAssertions.HasReasons(plan, "Foundation", new ConventionalCommitReason(SemverBump.Minor, [feature.Sha]));
			await PlanAssertions.HasReasons(plan, "Library", new DependencyReleaseReason("Foundation"));
			await PlanAssertions.Precedes(plan, "Foundation", "Library");
		}
	}

	[Test]
	public async Task ConditionalReferenceChangesOnlyTheSelectedConfiguration()
	{
		using RepositoryFixture fixture = new();
		fixture.AddProject("Foundation");
		fixture.AddProject("Library");
		fixture.AddReference("Library", "Foundation", configuration: "Staging");
		XDocument solution = XDocument.Load(fixture.SolutionPath);
		solution.Root!.AddFirst(new XElement("Configurations",
			new XElement("BuildType", new XAttribute("Name", "Release")),
			new XElement("BuildType", new XAttribute("Name", "Staging"))));
		solution.Save(fixture.SolutionPath);
		fixture.Commit("chore: add package graph", ".");
		fixture.Tag("Foundation", "1.2.3");
		fixture.Tag("Library", "2.3.4");

		fixture.Write("src/Foundation/Api.cs", "public static class Foundation { public static int Value => 43; }\n");
		Commit fix = fixture.Commit("fix: repair foundation API", "src/Foundation/Api.cs");
		ReleasePlan release = Generate(fixture);
		ReleasePlan staging = Generate(fixture, "Staging");

		using (Assert.Multiple())
		{
			await AssertPlan(release, "Release", ("Foundation", "Foundation"));
			await PlanAssertions.HasReleases(release, ("Foundation", "1.2.4", []));
			await PlanAssertions.HasReasons(release, "Foundation", new ConventionalCommitReason(SemverBump.Patch, [fix.Sha]));
			await AssertPlan(staging, "Staging", ("Foundation", "Foundation"), ("Library", "Library"));
			await PlanAssertions.HasReleases(staging, ("Foundation", "1.2.4", []), ("Library", "2.3.5", ["Foundation"]));
			await PlanAssertions.HasReasons(staging, "Foundation", new ConventionalCommitReason(SemverBump.Patch, [fix.Sha]));
			await PlanAssertions.HasReasons(staging, "Library", new DependencyReleaseReason("Foundation"));
			await PlanAssertions.Precedes(staging, "Foundation", "Library");
		}
	}

	[Test]
	public async Task EvaluatedPackageIdControlsTagLookupAndDependencyIdentity()
	{
		using RepositoryFixture fixture = new();
		fixture.AddProject("FoundationProject", "Foundation.Package");
		fixture.AddProject("LibraryProject", "Library.Package");
		fixture.AddReference("LibraryProject", "FoundationProject");
		fixture.Commit("chore: add package graph", ".");
		fixture.Tag("Foundation.Package", "1.2.3");
		fixture.Tag("Library.Package", "2.3.4");

		fixture.Write("src/FoundationProject/Api.cs", "public static class FoundationProject { public static int Value => 43; }\n");
		Commit feature = fixture.Commit("feat: extend foundation API", "src/FoundationProject/Api.cs");
		ReleasePlan plan = Generate(fixture);

		using (Assert.Multiple())
		{
			await AssertPlan(plan, "Release", ("Foundation.Package", "FoundationProject"),
				("Library.Package", "LibraryProject"));
			await PlanAssertions.HasReleases(plan, ("Foundation.Package", "1.3.0", []),
				("Library.Package", "2.3.5", ["Foundation.Package"]));
			await PlanAssertions.HasReasons(plan, "Foundation.Package", new ConventionalCommitReason(SemverBump.Minor, [feature.Sha]));
			await PlanAssertions.HasReasons(plan, "Library.Package", new DependencyReleaseReason("Foundation.Package"));
			await PlanAssertions.Precedes(plan, "Foundation.Package", "Library.Package");
		}
	}

	[Test]
	public async Task MultilineBreakingFooterCausesMajorReleaseAndDependantPatch()
	{
		using RepositoryFixture fixture = new();
		fixture.AddProject("Foundation");
		fixture.AddProject("Library");
		fixture.AddReference("Library", "Foundation");
		fixture.Commit("chore: add package graph", ".");
		fixture.Tag("Foundation", "1.2.3");
		fixture.Tag("Library", "2.3.4");

		fixture.Write("src/Foundation/Api.cs", "public static class Foundation { public static int Value => 43; }\n");
		Commit breaking = fixture.Commit("feat: change foundation API\n\nBREAKING CHANGE: callers must use the new API", "src/Foundation/Api.cs");
		ReleasePlan plan = Generate(fixture);

		using (Assert.Multiple())
		{
			await AssertPlan(plan, "Release", ("Foundation", "Foundation"), ("Library", "Library"));
			await PlanAssertions.HasReleases(plan, ("Foundation", "2.0.0", []), ("Library", "2.3.5", ["Foundation"]));
			await PlanAssertions.HasReasons(plan, "Foundation", new ConventionalCommitReason(SemverBump.Major, [breaking.Sha]));
			await PlanAssertions.HasReasons(plan, "Library", new DependencyReleaseReason("Foundation"));
			await PlanAssertions.Precedes(plan, "Foundation", "Library");
		}
	}

	[Test]
	public async Task SiblingTestsAndWorkflowChangesDoNotReleasePackage()
	{
		using RepositoryFixture fixture = new();
		fixture.AddProject("Foundation");
		fixture.Commit("chore: add foundation", ".");
		fixture.Tag("Foundation", "1.2.3");

		fixture.Write("src/Foundation.Tests/ApiTests.cs", "public static class ApiTests { }\n");
		fixture.Write(".github/workflows/ci.yml", "name: CI\n");
		fixture.Commit("feat!: change test and CI contract", "src/Foundation.Tests/ApiTests.cs", ".github/workflows/ci.yml");
		ReleasePlan plan = Generate(fixture);

		using (Assert.Multiple())
		{
			await AssertPlan(plan, "Release");
			await PlanAssertions.HasReleases(plan);
		}
	}

	[Test]
	public async Task UnreachableHigherVersionTagDoesNotReplaceReachableBaseline()
	{
		using RepositoryFixture fixture = new();
		fixture.AddProject("Foundation");
		fixture.AddProject("Library");
		fixture.AddReference("Library", "Foundation");
		fixture.Commit("chore: add package graph", ".");
		fixture.Tag("Foundation", "1.2.3");
		fixture.Tag("Library", "2.3.4");
		Branch original = fixture.Git.Head;
		Branch alternate = fixture.Git.CreateBranch("alternate-release");
		Commands.Checkout(fixture.Git, alternate);
		fixture.Write("other-branch.txt", "unreachable release\n");
		fixture.Commit("chore: alternate release", "other-branch.txt");
		fixture.Tag("Foundation", "9.0.0");
		Commands.Checkout(fixture.Git, original);

		fixture.Write("src/Foundation/Api.cs", "public static class Foundation { public static int Value => 43; }\n");
		Commit feature = fixture.Commit("feat: extend foundation API", "src/Foundation/Api.cs");
		ReleasePlan plan = Generate(fixture);

		using (Assert.Multiple())
		{
			await AssertPlan(plan, "Release", ("Foundation", "Foundation"), ("Library", "Library"));
			await PlanAssertions.HasReleases(plan, ("Foundation", "1.3.0", []), ("Library", "2.3.5", ["Foundation"]));
			await PlanAssertions.HasReasons(plan, "Foundation", new ConventionalCommitReason(SemverBump.Minor, [feature.Sha]));
			await PlanAssertions.HasReasons(plan, "Library", new DependencyReleaseReason("Foundation"));
			await PlanAssertions.Precedes(plan, "Foundation", "Library");
		}
	}

	private static ReleasePlan Generate(RepositoryFixture fixture, string configuration = "Release")
	{
		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		return planner.Generate(fixture.DirectoryPath, fixture.SolutionPath, configuration);
	}

	private static async Task AssertPlan(ReleasePlan plan, string configuration,
		params (string PackageId, string ProjectName)[] projects)
	{
		using (Assert.Multiple())
		{
			await Assert.That(plan.Configuration).IsEqualTo(configuration);
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await PlanAssertions.HasReferences(plan);
			foreach ((string packageId, string projectName) in projects)
			{
				PlannedRelease release = plan.Packages.Single(package => package.PackageId == packageId);
				await Assert.That(release.ProjectPath).IsEqualTo($"src/{projectName}/{projectName}.csproj");
			}
		}
	}
}
