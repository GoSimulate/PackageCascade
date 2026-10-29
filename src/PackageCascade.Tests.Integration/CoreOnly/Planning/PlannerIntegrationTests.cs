using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Public.DependencyInjection;
using PackageCascade.Public.Planning;
using PackageCascade.Public.Definition;

namespace PackageCascade.Tests.Integration.CoreOnly.Planning;

public sealed class PlannerIntegrationTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-planner-integration-").FullName;
	private readonly Repository _repository;

	public PlannerIntegrationTests()
	{
		Repository.Init(_directory);
		_repository = new(_directory);
	}

	[Test]
	public async Task PlansReleaseFromLatestTagThroughHeadUsingRegisteredPlanner()
	{
		string projectDirectory = Directory.CreateDirectory(Path.Combine(_directory, "src", "Fixture")).FullName;
		string solutionPath = Path.Combine(_directory, "Packages.slnx");
		await File.WriteAllTextAsync(solutionPath, """
		                                           <Solution>
		                                             <Project Path="src/Fixture/Fixture.csproj" />
		                                           </Solution>
		                                           """);
		await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Fixture.csproj"), """
		                                                                               <Project Sdk="Microsoft.NET.Sdk">
		                                                                                 <PropertyGroup>
		                                                                                   <TargetFramework>net11.0</TargetFramework>
		                                                                                 </PropertyGroup>
		                                                                               </Project>
		                                                                               """);

		Commit baseline = Commit("chore: initial project", "Packages.slnx", "src/Fixture/Fixture.csproj");
		_repository.Tags.Add("Fixture-v1.2.3", baseline);

		await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Api.cs"), "public static class Api { }\n");
		Commit("fix: repair API", "src/Fixture/Api.cs");

		await using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();

		ReleasePlan plan = planner.Generate(_directory, solutionPath);
		PlannedRelease release = plan.Packages.Single();

		using (Assert.Multiple())
		{
			await Assert.That(plan.Configuration).IsEqualTo("Release");
			await Assert.That(release.PackageId).IsEqualTo("Fixture");
			await Assert.That(release.Version).IsEqualTo("1.2.4");
			await Assert.That(release.Reasons.Any(reason => reason is ConventionalCommitReason { Bump: SemverBump.Patch })).IsTrue();
		}
	}

	[Test]
	public async Task EarlierRootSdkChangeRetainsMajorBumpAfterNestedGlobalJsonIsAdded()
	{
		string solutionDirectory = Directory.CreateDirectory(Path.Combine(_directory, "solutions", "nested")).FullName;
		string projectDirectory = Directory.CreateDirectory(Path.Combine(_directory, "src", "Fixture")).FullName;
		string solutionPath = Path.Combine(solutionDirectory, "Packages.slnx");
		await File.WriteAllTextAsync(solutionPath, """
			<Solution><Project Path="../../src/Fixture/Fixture.csproj" /></Solution>
			""");
		await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Fixture.csproj"), """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
			</Project>
			""");
		string rootGlobalJson = Path.Combine(_directory, "global.json");
		await File.WriteAllTextAsync(rootGlobalJson, """
			{"sdk":{"version":"11.0.100-rc.2.26504.105","allowPrerelease":true}}
			""");
		Commit baseline = Commit("chore: initial project", "global.json", "solutions/nested/Packages.slnx", "src/Fixture/Fixture.csproj");
		_repository.Tags.Add("Fixture-v1.2.3", baseline);

		await File.WriteAllTextAsync(rootGlobalJson, """
			{"sdk":{"version":"11.0.100-rc.2.26504.105","allowPrerelease":true,"rollForward":"latestPatch"}}
			""");
		Commit sdkChange = Commit("build!: change root SDK policy", "global.json");
		await File.WriteAllTextAsync(Path.Combine(solutionDirectory, "global.json"), """
			{"sdk":{"version":"11.0.100-rc.2.26504.105","allowPrerelease":true}}
			""");
		Commit("chore: add nested SDK policy", "solutions/nested/global.json");

		await using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		ReleasePlan plan = planner.Generate(_directory, solutionPath);
		PlannedRelease release = plan.Packages.Single();

		using (Assert.Multiple())
		{
			await Assert.That(release.Version).IsEqualTo("2.0.0");
			await Assert.That(release.Reasons.Any(reason => reason is ConventionalCommitReason
			{ Bump: SemverBump.Major } commitReason && commitReason.CommitShas.Contains(sdkChange.Sha))).IsTrue();
		}
	}

	public void Dispose()
	{
		_repository.Dispose();
		Directory.Delete(_directory, recursive: true);
	}

	private Commit Commit(string message, params string[] paths)
	{
		foreach (string path in paths)
		{
			Commands.Stage(_repository, path);
		}

		Signature author = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		return _repository.Commit(message, author, author);
	}
}
