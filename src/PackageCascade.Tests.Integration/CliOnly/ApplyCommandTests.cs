using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Cli;
using PackageCascade.Public.Definition;
using PackageCascade.Public.Executing;

namespace PackageCascade.Tests.Integration.CliOnly;

public sealed class ApplyCommandTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-apply-cli-").FullName;

	[Test]
	public async Task ApplyPassesPreparedPlanAndRequestedOptionsToExecutor(CancellationToken cancellationToken)
	{
		string planPath = await WritePlan(new([], "Staging", [], "src"), cancellationToken);
		string outputPath = Path.Combine(_directory, "packages");
		StubExecutor executor = new();
		TestBootstrap bootstrap = new(services => services.AddSingleton<IPackageCascadeExecutor>(executor));

		int exitCode = await Program.Run([
			"apply", "--output", outputPath, "--infile", planPath,
			"--repository", _directory, "--include-symbols",
		], bootstrap);

		using (Assert.Multiple())
		{
			await Assert.That(exitCode).IsEqualTo(0);
			await Assert.That(executor.Plan?.Configuration).IsEqualTo("Staging");
			await Assert.That(executor.Plan?.WorkingDirectory).IsEqualTo("src");
			await Assert.That(executor.RepositoryPath).IsEqualTo(_directory);
			await Assert.That(executor.Options).IsEqualTo(new ExecutionOptions(outputPath, true));
			await Assert.That(executor.CallCount).IsEqualTo(1);
		}
	}

	[Test]
	public async Task ApplyDefaultsRepositoryAndSymbolsWithoutChangingPlanConfiguration(CancellationToken cancellationToken)
	{
		string planPath = await WritePlan(new([], "Debug", [], "."), cancellationToken);
		StubExecutor executor = new();
		TestBootstrap bootstrap = new(services => services.AddSingleton<IPackageCascadeExecutor>(executor));

		int exitCode = await Program.Run([
			"apply", "--output", Path.Combine(_directory, "packages"), "--infile", planPath,
		], bootstrap);

		using (Assert.Multiple())
		{
			await Assert.That(exitCode).IsEqualTo(0);
			await Assert.That(executor.RepositoryPath).IsEqualTo(Directory.GetCurrentDirectory());
			await Assert.That(executor.Plan?.Configuration).IsEqualTo("Debug");
			await Assert.That(executor.Options?.IncludeSymbols).IsFalse();
			await Assert.That(executor.CallCount).IsEqualTo(1);
		}
	}

	[Test]
	public async Task ApplyRequiresOutputDirectoryBeforeExecuting(CancellationToken cancellationToken)
	{
		string planPath = await WritePlan(new([], "Release", [], "."), cancellationToken);
		StubExecutor executor = new();
		TestBootstrap bootstrap = new(services => services.AddSingleton<IPackageCascadeExecutor>(executor));

		int exitCode = await Program.Run(["apply", "--infile", planPath], bootstrap);

		using (Assert.Multiple())
		{
			await Assert.That(exitCode).IsNotEqualTo(0);
			await Assert.That(executor.CallCount).IsEqualTo(0);
		}
	}

	[Test]
	public async Task ApplyRejectsMalformedPlanBeforeExecuting(CancellationToken cancellationToken)
	{
		string path = Path.Combine(_directory, "broken-plan.json");
		await File.WriteAllTextAsync(path, "{invalid json", cancellationToken);
		StubExecutor executor = new();
		TestBootstrap bootstrap = new(services => services.AddSingleton<IPackageCascadeExecutor>(executor));

		int exitCode = await Program.Run(["apply", "--output", Path.Combine(_directory, "packages"), "--infile", path], bootstrap);
		await Assert.That(exitCode).IsNotEqualTo(0);

		await Assert.That(executor.CallCount).IsEqualTo(0);
	}

	public void Dispose() => Directory.Delete(_directory, recursive: true);

	private async Task<string> WritePlan(ReleasePlan plan, CancellationToken cancellationToken)
	{
		string path = Path.Combine(_directory, "plan.json");
		await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan), cancellationToken);
		return path;
	}

	private sealed class StubExecutor : IPackageCascadeExecutor
	{
		public ReleasePlan? Plan { get; private set; }
		public string? RepositoryPath { get; private set; }
		public ExecutionOptions? Options { get; private set; }
		public int CallCount { get; private set; }

		public Task<ExecutionResult> Execute(ReleasePlan plan, string repositoryPath,
			ExecutionOptions options, CancellationToken cancellationToken = default)
		{
			Plan = plan;
			RepositoryPath = repositoryPath;
			Options = options;
			CallCount++;
			return Task.FromResult(new ExecutionResult([], []));
		}
	}
}
