using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using LibGit2Sharp;
using PackageCascade.Cli;

namespace PackageCascade.Tests.Integration.CliEndToEnd;

public sealed class PrivateReferenceApplyProcessTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-private-reference-").FullName;
	private readonly Repository _repository;
	private readonly string _solutionPath;
	private readonly string _outputPath;

	public PrivateReferenceApplyProcessTests()
	{
		Repository.Init(_directory);
		_repository = new(_directory);
		_solutionPath = Path.Combine(_directory, "Packages.slnx");
		_outputPath = Path.Combine(_directory, "packages");

		File.WriteAllText(Path.Combine(_directory, ".gitignore"), "**/bin/\n**/obj/\n");
		File.WriteAllText(Path.Combine(_directory, "global.json"), """
			{"sdk":{"version":"11.0.100-rc.2.26504.105","allowPrerelease":true,"rollForward":"latestPatch"}}
			""");
		File.WriteAllText(Path.Combine(_directory, "NuGet.Config"),
			"<configuration><packageSources><clear /></packageSources></configuration>");
		File.WriteAllText(Path.Combine(_directory, "Directory.Build.props"),
			"<Project><PropertyGroup><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
		File.WriteAllText(_solutionPath, """
			<Solution>
			  <Project Path="A/A.csproj" />
			  <Project Path="B/B.csproj" />
			</Solution>
			""");

		string aDirectory = Directory.CreateDirectory(Path.Combine(_directory, "A")).FullName;
		string bDirectory = Directory.CreateDirectory(Path.Combine(_directory, "B")).FullName;
		File.WriteAllText(Path.Combine(aDirectory, "A.csproj"), """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
			  <ItemGroup><ProjectReference Include="../B/B.csproj" PrivateAssets="all" /></ItemGroup>
			</Project>
			""");
		File.WriteAllText(Path.Combine(aDirectory, "Api.cs"),
			"public static class A { public static int Value => B.Value; }\n");
		File.WriteAllText(Path.Combine(bDirectory, "B.csproj"), """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
			</Project>
			""");
		File.WriteAllText(Path.Combine(bDirectory, "Api.cs"),
			"public static class B { public static int Value => 42; }\n");

		Commit baseline = Commit("chore: add packages", ".gitignore", "global.json", "NuGet.Config",
			"Directory.Build.props", "Packages.slnx", "A/A.csproj", "A/Api.cs", "B/B.csproj", "B/Api.cs");
		_repository.Tags.Add("A-v2.0.0", baseline);
		_repository.Tags.Add("B-v1.2.3", baseline);

		File.WriteAllText(Path.Combine(bDirectory, "Api.cs"),
			"public static class B { public static int Value => 43; }\n");
		Commit("fix: repair B value", "B/Api.cs");
	}

	[Test]
	public async Task BuiltCliPipesPrivateReferencePlanToApply(CancellationToken cancellationToken)
	{
		using Process apply = StartProcess(redirectInput: true,
			"apply", "--repository", _directory, "--output", _outputPath);
		using Process plan = StartProcess(redirectInput: false,
			"plan", _solutionPath, "--repository", _directory);
		Task<string> planError = plan.StandardError.ReadToEndAsync(cancellationToken);
		Task<string> applyOutput = apply.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> applyError = apply.StandardError.ReadToEndAsync(cancellationToken);

		try
		{
			await plan.StandardOutput.BaseStream.CopyToAsync(apply.StandardInput.BaseStream, cancellationToken);
			apply.StandardInput.Close();
			await Task.WhenAll(plan.WaitForExitAsync(cancellationToken), apply.WaitForExitAsync(cancellationToken));
			using (Assert.Multiple())
			{
				await Assert.That(plan.ExitCode).IsEqualTo(0).Because(await planError);
				await Assert.That(apply.ExitCode).IsEqualTo(0).Because(await applyError);
			}

			await AssertApplied(await applyOutput, cancellationToken);
		}
		finally
		{
			await StopIfRunning(plan);
			await StopIfRunning(apply);
		}
	}

	[Test]
	public async Task BuiltCliAppliesPrivateReferencePlanFromFile(CancellationToken cancellationToken)
	{
		string planPath = Path.Combine(_directory, "plan.json");
		(int planExitCode, string planOutput, string planError) = await Run(cancellationToken,
			"plan", _solutionPath, "--repository", _directory, "--outfile", planPath);
		using (Assert.Multiple())
		{
			await Assert.That(planExitCode).IsEqualTo(0).Because(planError);
			await Assert.That(planOutput).IsEmpty();
			await Assert.That(File.Exists(planPath)).IsTrue();
		}

		(int applyExitCode, string applyOutput, string applyError) = await Run(cancellationToken,
			"apply", "--repository", _directory, "--infile", planPath, "--output", _outputPath);
		await Assert.That(applyExitCode).IsEqualTo(0).Because(applyError);
		await AssertApplied(applyOutput, cancellationToken);
	}

	private async Task AssertApplied(string output, CancellationToken cancellationToken)
	{
		using JsonDocument document = JsonDocument.Parse(output);
		JsonElement result = document.RootElement;
		string[] packagePaths = result.GetProperty("PackagePaths").EnumerateArray()
			.Select(path => path.GetString()!).ToArray();
		string[] createdTags = result.GetProperty("CreatedTags").EnumerateArray()
			.Select(tag => tag.GetString()!).ToArray();
		string[] expectedPaths = [
			Path.Combine(_outputPath, "B.1.2.4.nupkg"),
			Path.Combine(_outputPath, "A.2.0.1.nupkg"),
		];
		XElement bMetadata = await ReadMetadata(expectedPaths[0], cancellationToken);
		XElement aMetadata = await ReadMetadata(expectedPaths[1], cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(packagePaths).IsEquivalentTo(expectedPaths);
			await Assert.That(Directory.GetFiles(_outputPath, "*.nupkg")).IsEquivalentTo(expectedPaths);
			await Assert.That(createdTags).IsEquivalentTo(new[] { "B-v1.2.4", "A-v2.0.1" });
			await Assert.That(_repository.Tags["B-v1.2.4"]?.Target.Sha).IsEqualTo(_repository.Head.Tip!.Sha);
			await Assert.That(_repository.Tags["A-v2.0.1"]?.Target.Sha).IsEqualTo(_repository.Head.Tip!.Sha);
			await Assert.That(_repository.Tags["B-v1.2.3"]).IsNotNull();
			await Assert.That(_repository.Tags["A-v2.0.0"]).IsNotNull();
			await AssertIdentity(bMetadata, "B", "1.2.4");
			await AssertIdentity(aMetadata, "A", "2.0.1");
			await Assert.That(aMetadata.Descendants().Any(element => element.Name.LocalName == "dependency" &&
				(string?)element.Attribute("id") == "B")).IsFalse();
		}
	}

	private static async Task AssertIdentity(XElement metadata, string id, string version)
	{
		await Assert.That(metadata.Elements().Single(element => element.Name.LocalName == "id").Value).IsEqualTo(id);
		await Assert.That(metadata.Elements().Single(element => element.Name.LocalName == "version").Value).IsEqualTo(version);
	}

	private static async Task<XElement> ReadMetadata(string path, CancellationToken cancellationToken)
	{
		await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
			bufferSize: 4096, options: FileOptions.Asynchronous);
		await using ZipArchive archive = await ZipArchive.CreateAsync(file, ZipArchiveMode.Read,
			leaveOpen: true, entryNameEncoding: null, cancellationToken: cancellationToken);
		ZipArchiveEntry manifest = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
		await using Stream stream = await manifest.OpenAsync(cancellationToken);
		using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true });
		XDocument document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
		return document.Root!.Elements().Single(element => element.Name.LocalName == "metadata");
	}

	private Process StartProcess(bool redirectInput, params string[] arguments)
	{
		ProcessStartInfo startInfo = new("dotnet")
		{
			WorkingDirectory = _directory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = redirectInput,
		};
		startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		return Process.Start(startInfo)!;
	}

	private async Task<(int ExitCode, string Output, string Error)> Run(
		CancellationToken cancellationToken, params string[] arguments)
	{
		using Process process = StartProcess(redirectInput: false, arguments);
		Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
		try
		{
			await process.WaitForExitAsync(cancellationToken);
			return (process.ExitCode, await output, await error);
		}
		finally
		{
			await StopIfRunning(process);
		}
	}

	private static async Task StopIfRunning(Process process)
	{
		if (!process.HasExited)
		{
			process.Kill(entireProcessTree: true);
			await process.WaitForExitAsync(CancellationToken.None);
		}
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

	public void Dispose()
	{
		_repository.Dispose();
		Directory.Delete(_directory, recursive: true);
	}
}
