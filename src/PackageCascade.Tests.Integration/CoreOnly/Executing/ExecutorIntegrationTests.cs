using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using PackageCascade.Public.Definition;
using PackageCascade.Public.DependencyInjection;
using PackageCascade.Public.Executing;

namespace PackageCascade.Tests.Integration.CoreOnly.Executing;

public sealed class ExecutorIntegrationTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-executor-integration-").FullName;
	private readonly string _outputDirectory;
	private readonly Repository _repository;

	public ExecutorIntegrationTests()
	{
		Repository.Init(_directory);
		_repository = new(_directory);
		_outputDirectory = Path.Combine(_directory, "packages");

		File.WriteAllText(Path.Combine(_directory, "global.json"), """
			{"sdk":{"version":"11.0.100-rc.2.26504.105","allowPrerelease":true,"rollForward":"latestPatch"}}
			""");
		File.WriteAllText(Path.Combine(_directory, "NuGet.Config"), """
			<configuration><packageSources><clear /></packageSources></configuration>
			""");
		File.WriteAllText(Path.Combine(_directory, "Directory.Build.props"), """
			<Project><PropertyGroup><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
			""");
		WriteProject("B", "");
		WriteProject("A", """
			<PropertyGroup Condition="'$(Configuration)' == 'Staging'">
			  <Description>Staging package</Description>
			</PropertyGroup>
			<ItemGroup><ProjectReference Include="../B/B.csproj" /></ItemGroup>
			""");
		File.WriteAllText(Path.Combine(_directory, "A", "Api.cs"),
			"public static class A { public static int Value => B.Value; }\n");

		foreach (string path in new[] { "global.json", "NuGet.Config", "Directory.Build.props",
			"A/A.csproj", "A/Api.cs", "B/B.csproj", "B/Api.cs" })
		{
			Commands.Stage(_repository, path);
		}

		Signature author = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		_repository.Commit("chore: add packages", author, author);
	}

	[Test]
	public async Task AppliesDependencyFirstPlanWithIndependentVersions(CancellationToken cancellationToken)
	{
		ExecutionResult result = await Execute(Plan(), includeSymbols: false, cancellationToken);
		string[] expectedPaths = [
			Path.Combine(_outputDirectory, "B.3.4.5.nupkg"),
			Path.Combine(_outputDirectory, "A.2.3.4.nupkg"),
		];
		using (Assert.Multiple())
		{
			await Assert.That(result.PackagePaths).IsEquivalentTo(expectedPaths);
			await Assert.That(expectedPaths.All(File.Exists)).IsTrue();
			await Assert.That(result.CreatedTags).IsEquivalentTo(new[] { "B-v3.4.5", "A-v2.3.4" });
			await Assert.That(_repository.Tags["B-v3.4.5"]?.Target.Sha).IsEqualTo(_repository.Head.Tip!.Sha);
			await Assert.That(_repository.Tags["A-v2.3.4"]?.Target.Sha).IsEqualTo(_repository.Head.Tip!.Sha);
			await AssertIdentity(await ReadMetadata(expectedPaths[0], cancellationToken), "B", "3.4.5");
			await AssertIdentity(await ReadMetadata(expectedPaths[1], cancellationToken), "A", "2.3.4");
			await AssertDependency(await ReadMetadata(expectedPaths[1], cancellationToken));
		}
	}

	[Test]
	public async Task AppliesReleaseAgainstUnchangedReferencedPackage(CancellationToken cancellationToken)
	{
		ExecutionResult result = await Execute(Plan(includeB: false), includeSymbols: false, cancellationToken);
		string archivePath = Path.Combine(_outputDirectory, "A.2.3.4.nupkg");

		using (Assert.Multiple())
		{
			await Assert.That(result.PackagePaths).IsEquivalentTo(new[] { archivePath });
			await Assert.That(Directory.GetFiles(_outputDirectory, "*.nupkg")).IsEquivalentTo(new[] { archivePath });
			await Assert.That(result.CreatedTags).IsEquivalentTo(new[] { "A-v2.3.4" });
			await Assert.That(_repository.Tags["A-v2.3.4"]?.Target.Sha).IsEqualTo(_repository.Head.Tip!.Sha);
			await Assert.That(_repository.Tags["B-v3.4.5"]).IsNull();
			await AssertDependency(await ReadMetadata(archivePath, cancellationToken));
		}
	}

	[Test]
	public async Task UsesSelectedConfigurationAndProducesSymbols(CancellationToken cancellationToken)
	{
		ExecutionResult result = await Execute(Plan(configuration: "Staging"), includeSymbols: true, cancellationToken);
		string[] expectedPaths = [
			Path.Combine(_outputDirectory, "B.3.4.5.nupkg"),
			Path.Combine(_outputDirectory, "B.3.4.5.snupkg"),
			Path.Combine(_outputDirectory, "A.2.3.4.nupkg"),
			Path.Combine(_outputDirectory, "A.2.3.4.snupkg"),
		];
		XElement metadata = await ReadMetadata(expectedPaths[2], cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(result.PackagePaths).IsEquivalentTo(expectedPaths);
			await Assert.That(expectedPaths.All(File.Exists)).IsTrue();
			await Assert.That(metadata.Elements().Single(element => element.Name.LocalName == "description").Value)
				.IsEqualTo("Staging package");
			await AssertIdentity(await ReadMetadata(expectedPaths[1], cancellationToken), "B", "3.4.5");
			await AssertIdentity(await ReadMetadata(expectedPaths[3], cancellationToken), "A", "2.3.4");
		}
	}

	[Test]
	public async Task DoesNotTagWhenPackageIsMissingPlannedDependency(CancellationToken cancellationToken)
	{
		string projectPath = Path.Combine(_directory, "A", "A.csproj");
		XDocument project = XDocument.Parse(await File.ReadAllTextAsync(projectPath, cancellationToken));
		project.Root!.Add(new XElement("PropertyGroup", new XElement("SuppressDependenciesWhenPacking", "true")));
		await File.WriteAllTextAsync(projectPath, project.ToString(), cancellationToken);

		InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Execute(Plan(), includeSymbols: false, cancellationToken));

		using (Assert.Multiple())
		{
			await Assert.That(error!.Message).Contains("missing dependency 'B'");
			await Assert.That(_repository.Tags).IsEmpty();
			await Assert.That(Directory.Exists(_outputDirectory) && Directory.GetFiles(_outputDirectory).Length > 0).IsFalse();
		}
	}

	public void Dispose()
	{
		_repository.Dispose();
		Directory.Delete(_directory, recursive: true);
	}

	private static ReleasePlan Plan(bool includeB = true, string configuration = "Release")
	{
		PlannedRelease dependency = new("B", "B/B.csproj", "3.4.5", [new InitialReleaseReason()], []);
		PlannedRelease consumer = new("A", "A/A.csproj", "2.3.4", [new DependencyReleaseReason("B")], ["B"]);
		return includeB
			? new([dependency, consumer], configuration, [], ".")
			: new([consumer], configuration, [new ReferencedPackage("B", "B/B.csproj", "3.4.5")], ".");
	}

	private async Task<ExecutionResult> Execute(ReleasePlan plan, bool includeSymbols, CancellationToken cancellationToken)
	{
		await using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();
		IPackageCascadeExecutor executor = provider.GetRequiredService<IPackageCascadeExecutor>();
		return await executor.Execute(plan, _directory,
			new ExecutionOptions(_outputDirectory, IncludeSymbols: includeSymbols), cancellationToken);
	}

	private void WriteProject(string name, string references)
	{
		string directory = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
		File.WriteAllText(Path.Combine(directory, name + ".csproj"), $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net11.0</TargetFramework>
			    <DebugType>portable</DebugType>
			  </PropertyGroup>
			  {references}
			</Project>
			""");
		File.WriteAllText(Path.Combine(directory, "Api.cs"), $"public static class {name} {{ public static int Value => 42; }}\n");
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

	private static async Task AssertIdentity(XElement metadata, string id, string version)
	{
		await Assert.That(metadata.Elements().Single(element => element.Name.LocalName == "id").Value).IsEqualTo(id);
		await Assert.That(metadata.Elements().Single(element => element.Name.LocalName == "version").Value).IsEqualTo(version);
	}

	private static async Task AssertDependency(XElement metadata)
	{
		XElement dependency = metadata.Descendants().Single(element => element.Name.LocalName == "dependency");
		await Assert.That((string?)dependency.Attribute("id")).IsEqualTo("B");
		VersionRange range = VersionRange.Parse(dependency.Attribute("version")!.Value);
		await Assert.That(range.MinVersion).IsEqualTo(NuGetVersion.Parse("3.4.5"));
		await Assert.That(range.IsMinInclusive).IsTrue();
	}
}
