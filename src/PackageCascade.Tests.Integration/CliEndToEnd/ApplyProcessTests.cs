using System.Diagnostics;
using System.Text.Json;
using LibGit2Sharp;
using PackageCascade.Cli;

namespace PackageCascade.Tests.Integration.CliEndToEnd;

public sealed class ApplyProcessTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-cli-apply-").FullName;
	private readonly Repository _repository;
	private readonly string _solutionPath;

	public ApplyProcessTests()
	{
		Repository.Init(_directory);
		_repository = new(_directory);
		_solutionPath = Path.Combine(_directory, "Packages.slnx");

		File.WriteAllText(Path.Combine(_directory, ".gitignore"), "**/bin/\n**/obj/\n");
		File.WriteAllText(Path.Combine(_directory, "global.json"), """
			{"sdk":{"version":"11.0.100-rc.2.26504.105","allowPrerelease":true,"rollForward":"latestPatch"}}
			""");
		File.WriteAllText(Path.Combine(_directory, "NuGet.Config"),
			"<configuration><packageSources><clear /></packageSources></configuration>");
		File.WriteAllText(Path.Combine(_directory, "Directory.Build.props"),
			"<Project><PropertyGroup><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
		File.WriteAllText(_solutionPath, "<Solution><Project Path=\"Fixture/Fixture.csproj\" /></Solution>");
		string projectDirectory = Directory.CreateDirectory(Path.Combine(_directory, "Fixture")).FullName;
		File.WriteAllText(Path.Combine(projectDirectory, "Fixture.csproj"), """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup><TargetFramework>net11.0</TargetFramework><DebugType>portable</DebugType></PropertyGroup>
			</Project>
			""");
		File.WriteAllText(Path.Combine(projectDirectory, "Api.cs"),
			"public static class Fixture { public static int Value => 42; }\n");

		foreach (string path in new[] { ".gitignore", "global.json", "NuGet.Config", "Directory.Build.props",
		"Packages.slnx", "Fixture/Fixture.csproj", "Fixture/Api.cs" })
		{
			Commands.Stage(_repository, path);
		}

		Signature author = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		_repository.Commit("chore: add package", author, author);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task BuiltCliAppliesGeneratedPlanFromFileOrStandardInput(bool useStandardInput,
		CancellationToken cancellationToken)
	{
		string planPath = Path.Combine(_directory, "plan.json");
		string outputPath = Path.Combine(_directory, "packages");
		(int planExitCode, string planOutput, string planError) = useStandardInput
			? await Run(null, cancellationToken, "plan", _solutionPath, "--repository", _directory)
			: await Run(null, cancellationToken, "plan", _solutionPath, "--repository", _directory,
				"--outfile", planPath);
		await Assert.That(planExitCode).IsEqualTo(0).Because(planError);

		string[] applyArguments = useStandardInput
			? ["apply", "--repository", _directory, "--output", outputPath, "--include-symbols"]
			: ["apply", "--repository", _directory, "--infile", planPath, "--output", outputPath,
				"--include-symbols"];
		(int applyExitCode, string output, string applyError) = await Run(
			useStandardInput ? planOutput : null, cancellationToken, applyArguments);
		await Assert.That(applyExitCode).IsEqualTo(0).Because(applyError);

		using JsonDocument document = JsonDocument.Parse(output);
		JsonElement result = document.RootElement;
		string[] packagePaths = result.GetProperty("PackagePaths").EnumerateArray()
			.Select(path => path.GetString()!).ToArray();
		string[] createdTags = result.GetProperty("CreatedTags").EnumerateArray()
			.Select(tag => tag.GetString()!).ToArray();
		string[] expectedPaths = [
			Path.Combine(outputPath, "Fixture.0.1.0.nupkg"),
			Path.Combine(outputPath, "Fixture.0.1.0.snupkg"),
		];

		using (Assert.Multiple())
		{
			await Assert.That(packagePaths).IsEquivalentTo(expectedPaths);
			await Assert.That(expectedPaths.All(File.Exists)).IsTrue();
			await Assert.That(createdTags).IsEquivalentTo(new[] { "Fixture-v0.1.0" });
			await Assert.That(_repository.Tags["Fixture-v0.1.0"]?.Target.Sha)
				.IsEqualTo(_repository.Head.Tip!.Sha);
			await Assert.That(File.Exists(planPath)).IsEqualTo(!useStandardInput);
		}
	}

	private async Task<(int ExitCode, string Output, string Error)> Run(
		string? standardInput, CancellationToken cancellationToken, params string[] arguments)
	{
		ProcessStartInfo startInfo = new("dotnet")
		{
			WorkingDirectory = _directory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = standardInput is not null,
		};
		startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(startInfo)!;
		Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
		try
		{
			if (standardInput is not null)
			{
				await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
				process.StandardInput.Close();
			}

			await process.WaitForExitAsync(cancellationToken);
			return (process.ExitCode, await output, await error);
		}
		finally
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync(CancellationToken.None);
			}
		}
	}

	public void Dispose()
	{
		_repository.Dispose();
		Directory.Delete(_directory, recursive: true);
	}
}
