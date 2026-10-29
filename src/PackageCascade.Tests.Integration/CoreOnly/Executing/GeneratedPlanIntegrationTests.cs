using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Public.Definition;
using PackageCascade.Public.DependencyInjection;
using PackageCascade.Public.Executing;
using PackageCascade.Public.Planning;
using PackageCascade.Tests.Integration.Fixtures;

namespace PackageCascade.Tests.Integration.CoreOnly.Executing;

public sealed class GeneratedPlanIntegrationTests
{
	[Test]
	public async Task PlannerAndExecutorReleaseInitialDependencyChain(CancellationToken cancellationToken)
	{
		using RepositoryFixture fixture = new();
		fixture.AddProject("F");
		fixture.AddProject("A");
		fixture.AddReference("A", "F");
		fixture.Write("src/A/Api.cs", "public static class A { public static int Value => F.Value; }\n");
		fixture.Commit("chore: add packages", ".gitignore", "global.json", "NuGet.Config",
			"Directory.Build.props", "Packages.slnx", "src/F/F.csproj", "src/F/Api.cs",
			"src/A/A.csproj", "src/A/Api.cs");

		await using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadePlanner planner = provider.GetRequiredService<IPackageCascadePlanner>();
		IPackageCascadeExecutor executor = provider.GetRequiredService<IPackageCascadeExecutor>();
		ReleasePlan plan = planner.Generate(fixture.DirectoryPath, fixture.SolutionPath);
		using (Assert.Multiple())
		{
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.Version)))
				.IsEquivalentTo(new (string PackageId, string Version)[] { ("F", "0.1.0"), ("A", "0.1.0") });
			await Assert.That(plan.Packages[0].PackageId).IsEqualTo("F");
			await Assert.That(plan.Packages[0].DependencyIds).IsEmpty();
			await Assert.That(plan.Packages[1].DependencyIds).IsEquivalentTo(new[] { "F" });
			await Assert.That(plan.ReferencedPackages).IsEmpty();
		}

		ExecutionResult result = await executor.Execute(plan, fixture.DirectoryPath,
			new ExecutionOptions(fixture.OutputPath, IncludeSymbols: false), cancellationToken);
		string fArchive = Path.Combine(fixture.OutputPath, "F.0.1.0.nupkg");
		string aArchive = Path.Combine(fixture.OutputPath, "A.0.1.0.nupkg");
		using (Assert.Multiple())
		{
			await Assert.That(result.PackagePaths).IsEquivalentTo(new[] { fArchive, aArchive });
			await Assert.That(Directory.GetFiles(fixture.OutputPath)).IsEquivalentTo(new[] { fArchive, aArchive });
			await Assert.That(result.CreatedTags).IsEquivalentTo(new[] { "F-v0.1.0", "A-v0.1.0" });
			await Assert.That(fixture.Git.Tags["F-v0.1.0"]?.Target.Sha).IsEqualTo(fixture.Git.Head.Tip!.Sha);
			await Assert.That(fixture.Git.Tags["A-v0.1.0"]?.Target.Sha).IsEqualTo(fixture.Git.Head.Tip!.Sha);
		}
		using (Assert.Multiple())
		{
			await PackageArchiveAssertions.AssertArchive(fArchive, "F", "0.1.0", cancellationToken);
			await PackageArchiveAssertions.AssertArchive(aArchive, "A", "0.1.0", cancellationToken, ("F", "0.1.0"));
		}
	}
}
