using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Public.Definition;
using PackageCascade.Public.DependencyInjection;
using PackageCascade.Public.Planning;
using PackageCascade.Tests.Integration.Fixtures;

namespace PackageCascade.Tests.Integration.CoreOnly.Planning;

public sealed class PlannerIntegrationTests
{
	[Test]
	[Arguments("", SemverBump.None, "")]
	[Arguments("chore: tidy source", SemverBump.None, "")]
	[Arguments("fix: repair F", SemverBump.Patch, "1.2.4")]
	[Arguments("feat: extend F", SemverBump.Minor, "1.3.0")]
	[Arguments("feat!: replace F API", SemverBump.Major, "2.0.0")]
	public async Task CalculatesDirectBumpFromTaggedHistory(string message, SemverBump bump, string expectedVersion)
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		Commit? change = null;
		if (message.Length > 0)
		{
			repository.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
			change = repository.Commit(message, "src/F/Api.cs");
		}

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReferences(plan);
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			if (bump == SemverBump.None)
			{
				await PlanAssertions.HasReleases(plan);
			}
			else
			{
				await PlanAssertions.HasReleases(plan, ("F", expectedVersion, []));
				await PlanAssertions.HasReasons(plan, "F", new ConventionalCommitReason(bump, [change!.Sha]));
				await Assert.That(plan.Packages.Single().ProjectPath).IsEqualTo("src/F/F.csproj");
			}
		}
	}

	[Test]
	[Arguments("fix: change F", SemverBump.Patch, "1.2.4")]
	[Arguments("feat: change F", SemverBump.Minor, "1.3.0")]
	[Arguments("feat!: change F", SemverBump.Major, "2.0.0")]
	public async Task FoundationChangePatchesAllTransitiveDependants(string message, SemverBump bump, string expectedVersion)
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("A", "L");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
		Commit change = repository.Commit(message, "src/F/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("F", expectedVersion, []), ("L", "2.3.5", ["F"]), ("A", "3.4.6", ["L"]));
			await PlanAssertions.HasReferences(plan);
			await PlanAssertions.HasReasons(plan, "F", new ConventionalCommitReason(bump, [change.Sha]));
			await PlanAssertions.HasReasons(plan, "L", new DependencyReleaseReason("F"));
			await PlanAssertions.HasReasons(plan, "A", new DependencyReleaseReason("L"));
			await PlanAssertions.Precedes(plan, "F", "L");
			await PlanAssertions.Precedes(plan, "L", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task EarlierRootSdkChangeRetainsMajorBumpAfterNestedGlobalJsonIsAdded()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.Write("solutions/nested/Packages.slnx", "<Solution><Project Path=\"../../src/F/F.csproj\" /></Solution>");
		repository.Write("global.json", """
			{"sdk":{"version":"11.0.100-rc.2.26504.105","allowPrerelease":true}}
			""");
		repository.Commit("chore: initial project", ".");
		repository.Tag("F", "1.2.3");

		repository.Write("global.json", """
			{"sdk":{"version":"11.0.100-rc.2.26504.105","allowPrerelease":true,"rollForward":"latestPatch"}}
			""");
		Commit sdkChange = repository.Commit("build!: change root SDK policy", "global.json");
		repository.Write("solutions/nested/global.json", """
			{"sdk":{"version":"11.0.100-rc.2.26504.105","allowPrerelease":true}}
			""");
		repository.Commit("chore: add nested SDK policy", "solutions/nested/global.json");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, Path.Combine(repository.DirectoryPath, "solutions/nested/Packages.slnx"));
		PlannedRelease release = plan.Packages.Single();

		using (Assert.Multiple())
		{
			await Assert.That(release.Version).IsEqualTo("2.0.0");
			await Assert.That(release.Reasons.Any(reason => reason is ConventionalCommitReason
			{ Bump: SemverBump.Major } commitReason && commitReason.CommitShas.Contains(sdkChange.Sha))).IsTrue();
		}
	}

	[Test]
	[Arguments("fix: change L", SemverBump.Patch, "2.3.5")]
	[Arguments("feat: change L", SemverBump.Minor, "2.4.0")]
	[Arguments("feat!: change L", SemverBump.Major, "3.0.0")]
	public async Task MiddleChangeReleasesOnlyItsDependants(string message, SemverBump bump, string expectedVersion)
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("A", "L");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");
		repository.Write("src/L/Api.cs", "public static class L { public static int Value => 43; }\n");
		Commit change = repository.Commit(message, "src/L/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("L", expectedVersion, ["F"]), ("A", "3.4.6", ["L"]));
			await PlanAssertions.HasReferences(plan, new ReferencedPackage("F", "src/F/F.csproj", "1.2.3"));
			await PlanAssertions.HasReasons(plan, "L", new ConventionalCommitReason(bump, [change.Sha]));
			await PlanAssertions.HasReasons(plan, "A", new DependencyReleaseReason("L"));
			await PlanAssertions.Precedes(plan, "L", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	[Arguments("fix: change A", SemverBump.Patch, "3.4.6")]
	[Arguments("feat: change A", SemverBump.Minor, "3.5.0")]
	[Arguments("feat!: change A", SemverBump.Major, "4.0.0")]
	public async Task ConsumerChangeKeepsDependenciesAtTheirReleasedVersions(string message, SemverBump bump, string expectedVersion)
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("A", "L");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");
		repository.Write("src/A/Api.cs", "public static class A { public static int Value => 43; }\n");
		Commit change = repository.Commit(message, "src/A/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("A", expectedVersion, ["L"]));
			await PlanAssertions.HasReferences(plan, new ReferencedPackage("F", "src/F/F.csproj", "1.2.3"), new ReferencedPackage("L", "src/L/L.csproj", "2.3.4"));
			await PlanAssertions.HasReasons(plan, "A", new ConventionalCommitReason(bump, [change.Sha]));
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task DirectMinorWinsOverPropagatedPatchAndRetainsBothReasons()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("A", "L");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
		Commit foundation = repository.Commit("feat!: replace F API", "src/F/Api.cs");
		repository.Write("src/L/Api.cs", "public static class L { public static int Value => 43; }\n");
		Commit library = repository.Commit("feat: extend L", "src/L/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("F", "2.0.0", []), ("L", "2.4.0", ["F"]), ("A", "3.4.6", ["L"]));
			await PlanAssertions.HasReferences(plan);
			await PlanAssertions.HasReasons(plan, "F", new ConventionalCommitReason(SemverBump.Major, [foundation.Sha]));
			await PlanAssertions.HasReasons(plan, "L", new ConventionalCommitReason(SemverBump.Minor, [library.Sha]), new DependencyReleaseReason("F"));
			await PlanAssertions.HasReasons(plan, "A", new DependencyReleaseReason("L"));
			await PlanAssertions.Precedes(plan, "F", "L");
			await PlanAssertions.Precedes(plan, "L", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task DirectMajorWinsOverTransitivePatchAndRetainsBothReasons()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("A", "L");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
		Commit foundation = repository.Commit("feat: extend F", "src/F/Api.cs");
		repository.Write("src/A/Api.cs", "public static class A { public static int Value => 43; }\n");
		Commit application = repository.Commit("feat!: replace A API", "src/A/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("F", "1.3.0", []), ("L", "2.3.5", ["F"]), ("A", "4.0.0", ["L"]));
			await PlanAssertions.HasReferences(plan);
			await PlanAssertions.HasReasons(plan, "F", new ConventionalCommitReason(SemverBump.Minor, [foundation.Sha]));
			await PlanAssertions.HasReasons(plan, "L", new DependencyReleaseReason("F"));
			await PlanAssertions.HasReasons(plan, "A", new ConventionalCommitReason(SemverBump.Major, [application.Sha]), new DependencyReleaseReason("L"));
			await PlanAssertions.Precedes(plan, "F", "L");
			await PlanAssertions.Precedes(plan, "L", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task DiamondFoundationChangeReleasesConvergingConsumerOnce()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("R");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("R", "F");
		repository.AddReference("A", "L");
		repository.AddReference("A", "R");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("R", "5.6.7");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
		Commit change = repository.Commit("feat!: replace F API", "src/F/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("F", "2.0.0", []), ("L", "2.3.5", ["F"]), ("R", "5.6.8", ["F"]), ("A", "3.4.6", ["L", "R"]));
			await PlanAssertions.HasReferences(plan);
			await PlanAssertions.HasReasons(plan, "F", new ConventionalCommitReason(SemverBump.Major, [change.Sha]));
			await PlanAssertions.HasReasons(plan, "L", new DependencyReleaseReason("F"));
			await PlanAssertions.HasReasons(plan, "R", new DependencyReleaseReason("F"));
			await PlanAssertions.HasReasons(plan, "A", new DependencyReleaseReason("L"), new DependencyReleaseReason("R"));
			await PlanAssertions.Precedes(plan, "F", "L");
			await PlanAssertions.Precedes(plan, "F", "R");
			await PlanAssertions.Precedes(plan, "L", "A");
			await PlanAssertions.Precedes(plan, "R", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task DiamondBranchChangeLeavesSiblingUnreleased()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("R");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("R", "F");
		repository.AddReference("A", "L");
		repository.AddReference("A", "R");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("R", "5.6.7");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");
		repository.Write("src/L/Api.cs", "public static class L { public static int Value => 43; }\n");
		Commit change = repository.Commit("feat: extend L", "src/L/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("L", "2.4.0", ["F"]), ("A", "3.4.6", ["L", "R"]));
			await PlanAssertions.HasReferences(plan, new ReferencedPackage("F", "src/F/F.csproj", "1.2.3"), new ReferencedPackage("R", "src/R/R.csproj", "5.6.7"));
			await PlanAssertions.HasReasons(plan, "L", new ConventionalCommitReason(SemverBump.Minor, [change.Sha]));
			await PlanAssertions.HasReasons(plan, "A", new DependencyReleaseReason("L"));
			await PlanAssertions.Precedes(plan, "L", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task BothDiamondBranchesPatchConsumerOnlyOnce()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("R");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("R", "F");
		repository.AddReference("A", "L");
		repository.AddReference("A", "R");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("R", "5.6.7");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");
		repository.Write("src/L/Api.cs", "public static class L { public static int Value => 43; }\n");
		Commit left = repository.Commit("feat: extend L", "src/L/Api.cs");
		repository.Write("src/R/Api.cs", "public static class R { public static int Value => 43; }\n");
		Commit right = repository.Commit("feat!: replace R API", "src/R/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("L", "2.4.0", ["F"]), ("R", "6.0.0", ["F"]), ("A", "3.4.6", ["L", "R"]));
			await PlanAssertions.HasReferences(plan, new ReferencedPackage("F", "src/F/F.csproj", "1.2.3"));
			await PlanAssertions.HasReasons(plan, "L", new ConventionalCommitReason(SemverBump.Minor, [left.Sha]));
			await PlanAssertions.HasReasons(plan, "R", new ConventionalCommitReason(SemverBump.Major, [right.Sha]));
			await PlanAssertions.HasReasons(plan, "A", new DependencyReleaseReason("L"), new DependencyReleaseReason("R"));
			await PlanAssertions.Precedes(plan, "L", "A");
			await PlanAssertions.Precedes(plan, "R", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task DisconnectedComponentDoesNotEnterReleaseOrReferenceSet()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("A", "U");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
		Commit change = repository.Commit("fix: repair F", "src/F/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("F", "1.2.4", []), ("L", "2.3.5", ["F"]));
			await PlanAssertions.HasReferences(plan);
			await PlanAssertions.HasReasons(plan, "F", new ConventionalCommitReason(SemverBump.Patch, [change.Sha]));
			await PlanAssertions.HasReasons(plan, "L", new DependencyReleaseReason("F"));
			await PlanAssertions.Precedes(plan, "F", "L");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task DisconnectedComponentsRetainIndependentBumpsAndReasons()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("A", "U");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
		Commit foundation = repository.Commit("fix: repair F", "src/F/Api.cs");
		repository.Write("src/U/Api.cs", "public static class U { public static int Value => 43; }\n");
		Commit unrelated = repository.Commit("feat: extend U", "src/U/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("F", "1.2.4", []), ("L", "2.3.5", ["F"]), ("U", "4.6.0", []), ("A", "3.4.6", ["U"]));
			await PlanAssertions.HasReferences(plan);
			await PlanAssertions.HasReasons(plan, "F", new ConventionalCommitReason(SemverBump.Patch, [foundation.Sha]));
			await PlanAssertions.HasReasons(plan, "L", new DependencyReleaseReason("F"));
			await PlanAssertions.HasReasons(plan, "U", new ConventionalCommitReason(SemverBump.Minor, [unrelated.Sha]));
			await PlanAssertions.HasReasons(plan, "A", new DependencyReleaseReason("U"));
			await PlanAssertions.Precedes(plan, "F", "L");
			await PlanAssertions.Precedes(plan, "U", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task UntaggedChainStartsAtInitialVersionDespiteBreakingHistory()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("A");
		repository.AddReference("L", "F");
		repository.AddReference("A", "L");
		repository.Commit("chore: add packages", ".");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
		Commit change = repository.Commit("feat!: introduce F API", "src/F/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("F", "0.1.0", []), ("L", "0.1.0", ["F"]), ("A", "0.1.0", ["L"]));
			await PlanAssertions.HasReferences(plan);
			await PlanAssertions.HasReasons(plan, "F", new InitialReleaseReason());
			await PlanAssertions.HasReasons(plan, "L", new InitialReleaseReason(), new DependencyReleaseReason("F"));
			await PlanAssertions.HasReasons(plan, "A", new InitialReleaseReason(), new DependencyReleaseReason("L"));
			await PlanAssertions.Precedes(plan, "F", "L");
			await PlanAssertions.Precedes(plan, "L", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task UntaggedFoundationPatchesTaggedDependants()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("A", "L");
		repository.Commit("chore: add packages", ".");
		repository.Tag("L", "2.3.4");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("F", "0.1.0", []), ("L", "2.3.5", ["F"]), ("A", "3.4.6", ["L"]));
			await PlanAssertions.HasReferences(plan);
			await PlanAssertions.HasReasons(plan, "F", new InitialReleaseReason());
			await PlanAssertions.HasReasons(plan, "L", new DependencyReleaseReason("F"));
			await PlanAssertions.HasReasons(plan, "A", new DependencyReleaseReason("L"));
			await PlanAssertions.Precedes(plan, "F", "L");
			await PlanAssertions.Precedes(plan, "L", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task UntaggedConsumerReferencesTaggedDependenciesWithoutReleasingThem()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("A", "L");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("U", "4.5.6");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("A", "0.1.0", ["L"]));
			await PlanAssertions.HasReferences(plan, new ReferencedPackage("F", "src/F/F.csproj", "1.2.3"), new ReferencedPackage("L", "src/L/L.csproj", "2.3.4"));
			await PlanAssertions.HasReasons(plan, "A", new InitialReleaseReason());
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task SeveralDirectCommitsUseStrongestBumpWithoutAccumulatingVersions()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("L", "F");
		repository.AddReference("A", "L");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Tag("A", "3.4.5");
		repository.Tag("U", "4.5.6");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
		Commit patch = repository.Commit("fix: repair F", "src/F/Api.cs");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 44; }\n");
		Commit minor = repository.Commit("feat: extend F", "src/F/Api.cs");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 45; }\n");
		Commit major = repository.Commit("feat!: replace F API", "src/F/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("F", "2.0.0", []), ("L", "2.3.5", ["F"]), ("A", "3.4.6", ["L"]));
			await PlanAssertions.HasReferences(plan);
			await PlanAssertions.HasReasons(plan, "F", new ConventionalCommitReason(SemverBump.Major, [patch.Sha, minor.Sha, major.Sha]));
			await PlanAssertions.HasReasons(plan, "L", new DependencyReleaseReason("F"));
			await PlanAssertions.HasReasons(plan, "A", new DependencyReleaseReason("L"));
			await PlanAssertions.Precedes(plan, "F", "L");
			await PlanAssertions.Precedes(plan, "L", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task EachPackageUsesItsOwnLatestTagBaseline()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("L");
		repository.AddReference("L", "F");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("L", "2.3.4");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
		repository.Write("src/L/Api.cs", "public static class L { public static int Value => 43; }\n");
		Commit feature = repository.Commit("feat: extend both APIs", "src/F/Api.cs", "src/L/Api.cs");
		repository.Tag("L", "2.4.0");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("F", "1.3.0", []), ("L", "2.4.1", ["F"]));
			await PlanAssertions.HasReferences(plan);
			await PlanAssertions.HasReasons(plan, "F", new ConventionalCommitReason(SemverBump.Minor, [feature.Sha]));
			await PlanAssertions.HasReasons(plan, "L", new DependencyReleaseReason("F"));
			await PlanAssertions.Precedes(plan, "F", "L");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

	[Test]
	public async Task FoundationPatchTraversesSixPackagesInDependencyOrder()
	{
		using RepositoryFixture repository = new();
		repository.AddProject("F");
		repository.AddProject("E");
		repository.AddProject("D");
		repository.AddProject("C");
		repository.AddProject("B");
		repository.AddProject("A");
		repository.AddProject("U");
		repository.AddReference("E", "F");
		repository.AddReference("D", "E");
		repository.AddReference("C", "D");
		repository.AddReference("B", "C");
		repository.AddReference("A", "B");
		repository.Commit("chore: add packages", ".");
		repository.Tag("F", "1.2.3");
		repository.Tag("E", "2.3.4");
		repository.Tag("D", "3.4.5");
		repository.Tag("C", "4.5.6");
		repository.Tag("B", "5.6.7");
		repository.Tag("A", "6.7.8");
		repository.Tag("U", "7.8.9");
		repository.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
		Commit change = repository.Commit("fix: repair F", "src/F/Api.cs");

		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(repository.DirectoryPath, repository.SolutionPath);
		using (Assert.Multiple())
		{
			await PlanAssertions.HasReleases(plan, ("F", "1.2.4", []), ("E", "2.3.5", ["F"]), ("D", "3.4.6", ["E"]), ("C", "4.5.7", ["D"]), ("B", "5.6.8", ["C"]), ("A", "6.7.9", ["B"]));
			await PlanAssertions.HasReferences(plan);
			await PlanAssertions.HasReasons(plan, "F", new ConventionalCommitReason(SemverBump.Patch, [change.Sha]));
			await PlanAssertions.HasReasons(plan, "E", new DependencyReleaseReason("F"));
			await PlanAssertions.HasReasons(plan, "D", new DependencyReleaseReason("E"));
			await PlanAssertions.HasReasons(plan, "C", new DependencyReleaseReason("D"));
			await PlanAssertions.HasReasons(plan, "B", new DependencyReleaseReason("C"));
			await PlanAssertions.HasReasons(plan, "A", new DependencyReleaseReason("B"));
			await PlanAssertions.Precedes(plan, "F", "E");
			await PlanAssertions.Precedes(plan, "E", "D");
			await PlanAssertions.Precedes(plan, "D", "C");
			await PlanAssertions.Precedes(plan, "C", "B");
			await PlanAssertions.Precedes(plan, "B", "A");
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(plan.WorkingDirectory).IsEqualTo(".");
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.ProjectPath)))
				.IsEquivalentTo(plan.Packages.Select(package => (package.PackageId, $"src/{package.PackageId}/{package.PackageId}.csproj")));
		}
	}

}
