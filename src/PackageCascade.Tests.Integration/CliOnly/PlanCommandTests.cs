using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Cli;
using PackageCascade.Public.Planning;
using PackageCascade.Public.Planning.Definition;
using System.Text.Json;

namespace PackageCascade.Tests.Integration.CliOnly;

public sealed class PlanCommandTests : IDisposable
{
	private readonly string _directory;

	public PlanCommandTests() => _directory = Directory.CreateTempSubdirectory("packagecascade-cli-only-").FullName;

	[Test]
	public async Task PlanUsesTheRegisteredPlannerWithCommandPaths()
	{
		StubPlanner planner = new();
		TestBootstrap bootstrap = new(services => services.AddSingleton<IPackageCascadePlanner>(planner));

		int exitCode = await Program.Run(["plan", "Packages.slnx", "--repository", _directory], bootstrap);

		using (Assert.Multiple())
		{
			await Assert.That(exitCode).IsEqualTo(0);
			await Assert.That(planner.RepositoryPath).IsEqualTo(_directory);
			await Assert.That(planner.SolutionPath).IsEqualTo("Packages.slnx");
			await Assert.That(planner.Configuration).IsEqualTo("Release");
		}
	}

	[Test]
	[Arguments("Debug")]
	[Arguments("Staging")]
	public async Task PlanPassesRequestedConfigurationToPlanner(string configuration)
	{
		StubPlanner planner = new();
		TestBootstrap bootstrap = new(services => services.AddSingleton<IPackageCascadePlanner>(planner));

		int exitCode = await Program.Run(["plan", "Packages.slnx", "--configuration", configuration], bootstrap);

		using (Assert.Multiple())
		{
			await Assert.That(exitCode).IsEqualTo(0);
			await Assert.That(planner.Configuration).IsEqualTo(configuration);
		}
	}

	[Test]
	public async Task PlanDefaultsRepositoryToCurrentDirectory()
	{
		StubPlanner planner = new();
		TestBootstrap bootstrap = new(services => services.AddSingleton<IPackageCascadePlanner>(planner));

		int exitCode = await Program.Run(["plan", "Packages.slnx"], bootstrap);

		using (Assert.Multiple())
		{
			await Assert.That(exitCode).IsEqualTo(0);
			await Assert.That(planner.RepositoryPath).IsEqualTo(Directory.GetCurrentDirectory());
			await Assert.That(planner.SolutionPath).IsEqualTo("Packages.slnx");
		}
	}

	[Test]
	public async Task PlanWritesReadableVersionsAndReasonTypes()
	{
		ReleasePlan plan = new(
			[new PlannedRelease("Fixture", "src/Fixture/Fixture.csproj", "1.2.4",
				[new ConventionalCommitReason(SemverBump.Patch, ["abc"]),
					new DependencyReleaseReason("Foundation")])], "target-sha", "Release");
		StubPlanner planner = new(plan);
		TestBootstrap bootstrap = new(services => services.AddSingleton<IPackageCascadePlanner>(planner));
		string outputPath = Path.Combine(_directory, "plan.json");

		int exitCode = await Program.Run(["plan", "Packages.slnx", "--outfile", outputPath], bootstrap);

		await Assert.That(exitCode).IsEqualTo(0);

		using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
		JsonElement output = document.RootElement;
		JsonElement package = output.GetProperty("Packages").EnumerateArray().Single();

		using (Assert.Multiple())
		{
			await Assert.That(package.GetProperty("Version").GetString()).IsEqualTo("1.2.4");
			await Assert.That(package.GetProperty("ProjectPath").GetString()).IsEqualTo("src/Fixture/Fixture.csproj");
			await Assert.That(package.GetProperty("Reasons").EnumerateArray().Select(reason => reason.GetProperty("Type").GetString()!)).IsEquivalentTo(["ConventionalCommit", "DependencyRelease"]);
		}
	}

	public void Dispose() => Directory.Delete(_directory, recursive: true);

	private sealed class StubPlanner : IPackageCascadePlanner
	{
		private readonly ReleasePlan? _plan;

		public StubPlanner(ReleasePlan? plan = null) => _plan = plan;

		public string? RepositoryPath { get; private set; }
		public string? SolutionPath { get; private set; }
		public string? Configuration { get; private set; }

		public ReleasePlan Generate(string repositoryPath, string solutionPath, string configuration = "Release")
		{
			RepositoryPath = repositoryPath;
			SolutionPath = solutionPath;
			Configuration = configuration;

			return _plan ?? new([], "target-sha", configuration);
		}
	}
}
