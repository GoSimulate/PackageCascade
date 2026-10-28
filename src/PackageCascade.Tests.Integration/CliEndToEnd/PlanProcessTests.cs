using System.Diagnostics;
using System.Text.Json;
using LibGit2Sharp;
using PackageCascade.Cli;

namespace PackageCascade.Tests.Integration.CliEndToEnd;

public sealed class PlanProcessTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-cli-plan-").FullName;
	private readonly string _solutionPath;
	private readonly Repository _repository;

	public PlanProcessTests()
	{
		Repository.Init(_directory);
		_repository = new(_directory);

		string projectDirectory = Directory.CreateDirectory(Path.Combine(_directory, "src", "Fixture")).FullName;
		_solutionPath = Path.Combine(_directory, "Packages.slnx");
		File.WriteAllText(_solutionPath, """
		                                 <Solution>
		                                   <Project Path="src/Fixture/Fixture.csproj" />
		                                 </Solution>
		                                 """);
		File.WriteAllText(Path.Combine(projectDirectory, "Fixture.csproj"), """
		                                                                   <Project Sdk="Microsoft.NET.Sdk">
		                                                                     <PropertyGroup>
		                                                                       <TargetFramework>net11.0</TargetFramework>
		                                                                     </PropertyGroup>
		                                                                   </Project>
		                                                                   """);

		Commands.Stage(_repository, "Packages.slnx");
		Commands.Stage(_repository, "src/Fixture/Fixture.csproj");
		Signature author = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		_repository.Commit("chore: add package", author, author);
	}

	[Test]
	public async Task BuiltCliWritesPlanToStandardOutput()
	{
		(int exitCode, string output, string error) = await Run();

		await Assert.That(exitCode).IsEqualTo(0).Because(error);

		using JsonDocument document = JsonDocument.Parse(output);
		await AssertPlanShape(document.RootElement);
	}

	[Test]
	public async Task BuiltCliWritesPlanOnlyToSpecifiedFile()
	{
		string outputPath = Path.Combine(_directory, "plan.json");

		(int exitCode, string output, string error) = await Run("--outfile", outputPath);

		using (Assert.Multiple())
		{
			await Assert.That(exitCode).IsEqualTo(0).Because(error);
			await Assert.That(output).IsEmpty();
			await Assert.That(File.Exists(outputPath)).IsTrue();
		}

		using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
		await AssertPlanShape(document.RootElement);
	}

	private async Task<(int ExitCode, string Output, string Error)> Run(params string[] extraArguments)
	{
		ProcessStartInfo startInfo = new("dotnet")
		{
			WorkingDirectory = _directory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
		startInfo.ArgumentList.Add("plan");
		startInfo.ArgumentList.Add(_solutionPath);
		foreach (string argument in extraArguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(startInfo)!;
		Task<string> output = process.StandardOutput.ReadToEndAsync();
		Task<string> error = process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();
		await Task.WhenAll(output, error);

		return (process.ExitCode, await output, await error);
	}

	private static async Task AssertPlanShape(JsonElement plan)
	{
		JsonElement package = plan.GetProperty("Packages").EnumerateArray().Single();

		using (Assert.Multiple())
		{
			await Assert.That(package.GetProperty("PackageId").GetString()).IsEqualTo("Fixture");
			await Assert.That(package.GetProperty("Version").GetString()).IsNotEmpty();
			await Assert.That(package.GetProperty("ProjectPath").GetString()).IsEqualTo("src/Fixture/Fixture.csproj");
			await Assert.That(package.GetProperty("Reasons").EnumerateArray().Single().GetProperty("Type").GetString()).IsEqualTo("InitialRelease");
		}
	}

	public void Dispose()
	{
		_repository.Dispose();
		Directory.Delete(_directory, recursive: true);
	}
}
