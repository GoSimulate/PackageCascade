using System.IO.Compression;
using System.Xml.Linq;
using NuGet.Versioning;
using PackageCascade.Internal.Executing.Packaging;

namespace PackageCascade.Tests.Unit.Internal.Executing.Packaging;

public sealed class PackageBuilderTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-versioned-sdk-").FullName;
	private readonly string _solutionPath;
	private readonly string _projectA;
	private readonly string _projectB;
	private readonly string _outputDirectory;

	public PackageBuilderTests()
	{
		File.WriteAllText(Path.Combine(_directory, "global.json"), """
			{"sdk":{"version":"11.0.100-rc.2.26504.105","allowPrerelease":true,"rollForward":"latestPatch"}}
			""");
		File.WriteAllText(Path.Combine(_directory, "NuGet.Config"), """
			<configuration><packageSources><clear /></packageSources></configuration>
			""");
		File.WriteAllText(Path.Combine(_directory, "Directory.Build.props"), """
			<Project>
			  <PropertyGroup>
			    <NuGetAudit>false</NuGetAudit>
			    <DefineConstants>$(DefineConstants);ORIGINAL_PROPS</DefineConstants>
			  </PropertyGroup>
			</Project>
			""");
		File.WriteAllText(Path.Combine(_directory, "Directory.Build.targets"), """
			<Project>
			  <PropertyGroup>
			    <DefineConstants>$(DefineConstants);ORIGINAL_TARGETS</DefineConstants>
			  </PropertyGroup>
			</Project>
			""");
		_projectB = WriteProject("B", "<TargetFramework>net11.0</TargetFramework>", "");
		_projectA = WriteProject("A", "<TargetFramework>net11.0</TargetFramework>", """
			<ItemGroup><ProjectReference Include="../B/B.csproj" /></ItemGroup>
			""");
		File.WriteAllText(Path.Combine(_directory, "A", "Api.cs"), """
			#if !ORIGINAL_PROPS || !ORIGINAL_TARGETS
			#error Original directory build imports must be preserved.
			#endif
			public static class A { public static int Value => B.Value; }
			""");
		_solutionPath = Path.Combine(_directory, "Packages.slnx");
		File.WriteAllText(_solutionPath, """
			<Solution><Project Path="A/A.csproj" /><Project Path="B/B.csproj" /></Solution>
			""");
		_outputDirectory = Directory.CreateDirectory(Path.Combine(_directory, "packages")).FullName;
	}

	[Test]
	public async Task PacksIndependentVersionsAndPreservesDirectoryBuildImports()
	{
		IReadOnlyList<string> archives = await new PackageBuilder().Build(Input([_projectB, _projectA]), CancellationToken.None);

		using (Assert.Multiple())
		{
			await Assert.That(archives.Select(path => Path.GetFileName(path)!)).IsEquivalentTo(new[] { "A.2.3.4.nupkg", "B.3.4.5.nupkg" });
			await Assert.That(Directory.GetFiles(_outputDirectory, "*.snupkg")).IsEmpty();
			await AssertIdentity(ReadManifest("A.2.3.4.nupkg"), "A", "2.3.4");
			await AssertIdentity(ReadManifest("B.3.4.5.nupkg"), "B", "3.4.5");
			await AssertMinimumDependency(ReadManifest("A.2.3.4.nupkg").Descendants().Single(element => element.Name.LocalName == "dependency"), "B", "3.4.5");
		}
	}

	[Test]
	public async Task IgnoresUnrelatedSolutionProjects()
	{
		string unrelated = WriteProject("Unrelated", "<TargetFramework>net11.0</TargetFramework>",
			"<Import Project=\"missing.targets\" />");
		XDocument solution = XDocument.Load(_solutionPath);
		solution.Root!.Add(new XElement("Project", new XAttribute("Path", "Unrelated/Unrelated.csproj")));
		solution.Save(_solutionPath);

		IReadOnlyList<string> archives = await new PackageBuilder().Build(Input([_projectB, _projectA]), CancellationToken.None);

		using (Assert.Multiple())
		{
			await Assert.That(archives.Select(path => Path.GetFileName(path)!)).IsEquivalentTo(new[] { "A.2.3.4.nupkg", "B.3.4.5.nupkg" });
			await Assert.That(Directory.Exists(Path.Combine(Path.GetDirectoryName(unrelated)!, "obj"))).IsFalse();
		}
	}

	[Test]
	[Arguments("A", "B")]
	[Arguments("B", "A")]
	public async Task BuildsSelectedProjectsInRequestedOrder(string first, string second)
	{
		WriteProject("A", "<TargetFramework>net11.0</TargetFramework>", "");
		string orderPath = Path.Combine(_directory, "build-order.txt");
		XDocument targets = XDocument.Load(Path.Combine(_directory, "Directory.Build.targets"));
		targets.Root!.Add(new XElement("Target",
			new XAttribute("Name", "RecordBuildOrder"),
			new XAttribute("BeforeTargets", "CoreCompile"),
			new XElement("WriteLinesToFile",
				new XAttribute("File", orderPath),
				new XAttribute("Lines", "$(MSBuildProjectName)"))));
		targets.Save(Path.Combine(_directory, "Directory.Build.targets"));
		string[] projects = [Path.Combine(_directory, first, first + ".csproj"), Path.Combine(_directory, second, second + ".csproj")];

		await new PackageBuilder().Build(Input(projects), CancellationToken.None);

		await Assert.That(File.ReadAllLines(orderPath).SequenceEqual([first, second])).IsTrue();
	}

	[Test]
	public async Task PacksOnlySelectedProjectAgainstUnchangedDependencyAndIncludesSymbols()
	{
		IReadOnlyList<string> archives = await new PackageBuilder().Build(Input([_projectA], includeSymbols: true), CancellationToken.None);

		using (Assert.Multiple())
		{
			await Assert.That(archives.Select(path => Path.GetFileName(path)!)).IsEquivalentTo(new[] { "A.2.3.4.nupkg", "A.2.3.4.snupkg" });
			await AssertIdentity(ReadManifest("A.2.3.4.nupkg"), "A", "2.3.4");
			await AssertIdentity(ReadManifest("A.2.3.4.snupkg"), "A", "2.3.4");
			await AssertMinimumDependency(ReadManifest("A.2.3.4.nupkg").Descendants().Single(element => element.Name.LocalName == "dependency"), "B", "3.4.5");
			using ZipArchive symbols = ZipFile.OpenRead(Path.Combine(_outputDirectory, "A.2.3.4.snupkg"));
			await Assert.That(symbols.Entries.Any(entry => entry.FullName.EndsWith(".pdb", StringComparison.Ordinal))).IsTrue();
		}
	}

	[Test]
	public async Task PacksMappedVersionsAcrossFrameworksWithConditionalDependency()
	{
		WriteProject("A", "<TargetFrameworks>net11.0;net11.0-windows</TargetFrameworks>", """
			<ItemGroup Condition="'$(TargetFramework)' == 'net11.0-windows'"><ProjectReference Include="../B/B.csproj" /></ItemGroup>
			""");
		File.WriteAllText(Path.Combine(_directory, "A", "Api.cs"), """
			#if !ORIGINAL_PROPS || !ORIGINAL_TARGETS
			#error Original directory build imports must be preserved.
			#endif
			public static class A
			{
			#if NET11_0_WINDOWS
			    public static int Value => B.Value;
			#else
			    public static int Value => 10;
			#endif
			}
			""");

		await new PackageBuilder().Build(Input([_projectB, _projectA]), CancellationToken.None);

		XDocument manifest = ReadManifest("A.2.3.4.nupkg");
		using (Assert.Multiple())
		{
			await AssertIdentity(manifest, "A", "2.3.4");
			XElement net11 = manifest.Descendants().Single(element => element.Name.LocalName == "group" && (string?)element.Attribute("targetFramework") == "net11.0");
			XElement windows = manifest.Descendants().Single(element => element.Name.LocalName == "group" && (string?)element.Attribute("targetFramework") == "net11.0-windows7.0");
			await Assert.That(net11.Elements().Where(element => element.Name.LocalName == "dependency")).IsEmpty();
			await AssertMinimumDependency(windows.Elements().Single(element => element.Name.LocalName == "dependency"), "B", "3.4.5");
			await AssertIdentity(ReadManifest("B.3.4.5.nupkg"), "B", "3.4.5");
		}
	}

	[Test]
	public async Task LateVersionOverrideIsRejectedBeforePacking()
	{
		XDocument targets = XDocument.Load(Path.Combine(_directory, "Directory.Build.targets"));
		targets.Root!.Add(new XElement("PropertyGroup",
			new XAttribute("Condition", "'$(MSBuildProjectName)' == 'A'"),
			new XElement("PackageVersion", "7.6.5")));
		targets.Save(Path.Combine(_directory, "Directory.Build.targets"));

		InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => new PackageBuilder().Build(Input([_projectB, _projectA]), CancellationToken.None));

		using (Assert.Multiple())
		{
			await Assert.That(exception!.Message).Contains("7.6.5");
			await Assert.That(Directory.GetFiles(_directory, "*.packagecascade-*.targets", SearchOption.AllDirectories)).IsEmpty();
			await Assert.That(exception!.Message).Contains("2.3.4");
			await Assert.That(Directory.GetFiles(_outputDirectory)).IsEmpty();
		}
	}

	[Test]
	public async Task PreservesExistingVersionHookAndCleansTemporaryImports()
	{
		string hookPath = Path.Combine(_directory, "consumer.targets");
		File.WriteAllText(hookPath, """
			<Project><PropertyGroup><DefineConstants>$(DefineConstants);CONSUMER_HOOK</DefineConstants></PropertyGroup></Project>
			""");
		XDocument project = XDocument.Load(_projectA);
		project.Root!.Add(new XElement("PropertyGroup",
			new XAttribute("Condition", "'$(Configuration)' == 'Release'"),
			new XElement("BeforeMicrosoftNETSdkTargets", hookPath)));
		project.Save(_projectA);
		File.WriteAllText(Path.Combine(_directory, "A", "Api.cs"), """
			#if !ORIGINAL_PROPS || !ORIGINAL_TARGETS || !CONSUMER_HOOK
			#error Consumer imports must be preserved.
			#endif
			public static class A { public static int Value => B.Value; }
			""");

		await new PackageBuilder().Build(Input([_projectB, _projectA]), CancellationToken.None);

		using (Assert.Multiple())
		{
			await AssertIdentity(ReadManifest("A.2.3.4.nupkg"), "A", "2.3.4");
			await Assert.That(File.ReadAllText(Path.Combine(_directory, "A", "obj", "Release", "net11.0", "A.AssemblyInfo.cs")))
				.Contains("AssemblyVersionAttribute(\"2.3.4.0\")");
			await Assert.That(Directory.GetFiles(_directory, "*.packagecascade-*.targets", SearchOption.AllDirectories)).IsEmpty();
			await Assert.That(File.Exists(hookPath)).IsTrue();
		}
	}

	[Test]
	public async Task PacksProjectUnderPathContainingApostropheAndSpecialCharacters()
	{
		string projectDirectory = Path.Combine(_directory, "O'Brien percent% dollar$ brackets[]");
		Directory.Move(Path.GetDirectoryName(_projectA)!, projectDirectory);
		string projectPath = Path.Combine(projectDirectory, "A.csproj");
		PackageBuildInput input = Input([_projectB, projectPath]) with
		{
			VersionsByProjectPath = new Dictionary<string, string>
			{
				[projectPath] = "2.3.4",
				[_projectB] = "3.4.5",
			},
		};

		await new PackageBuilder().Build(input, CancellationToken.None);

		using (Assert.Multiple())
		{
			await AssertIdentity(ReadManifest("A.2.3.4.nupkg"), "A", "2.3.4");
			await AssertMinimumDependency(ReadManifest("A.2.3.4.nupkg").Descendants().Single(element => element.Name.LocalName == "dependency"), "B", "3.4.5");
			await Assert.That(Directory.GetFiles(_directory, "*.packagecascade-*.targets", SearchOption.AllDirectories)).IsEmpty();
		}
	}

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	private PackageBuildInput Input(IReadOnlyList<string> selectedProjects, bool includeSymbols = false)
	{
		return new(_solutionPath, selectedProjects, new Dictionary<string, string>
		{
			[_projectA] = "2.3.4",
			[_projectB] = "3.4.5",
		}, "Release", includeSymbols, _outputDirectory);
	}

	private string WriteProject(string name, string framework, string references)
	{
		string directory = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
		string path = Path.Combine(directory, name + ".csproj");
		File.WriteAllText(path, $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    {framework}
			    <Version>8.8.8</Version>
			    <PackageVersion>9.9.9</PackageVersion>
			    <DebugType>portable</DebugType>
			  </PropertyGroup>
			  {references}
			</Project>
			""");
		File.WriteAllText(Path.Combine(directory, "Api.cs"), $$"""
			#if !ORIGINAL_PROPS || !ORIGINAL_TARGETS
			#error Original directory build imports must be preserved.
			#endif
			public static class {{name}} { public static int Value => 42; }
			""");
		return path;
	}

	private XDocument ReadManifest(string fileName)
	{
		using ZipArchive archive = ZipFile.OpenRead(Path.Combine(_outputDirectory, fileName));
		using Stream stream = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
		return XDocument.Load(stream);
	}

	private static async Task AssertIdentity(XDocument manifest, string id, string version)
	{
		XElement metadata = manifest.Root!.Elements().Single(element => element.Name.LocalName == "metadata");
		await Assert.That(metadata.Elements().Single(element => element.Name.LocalName == "id").Value).IsEqualTo(id);
		await Assert.That(NuGetVersion.Parse(metadata.Elements().Single(element => element.Name.LocalName == "version").Value)).IsEqualTo(NuGetVersion.Parse(version));
	}

	private static async Task AssertMinimumDependency(XElement dependency, string id, string version)
	{
		await Assert.That((string?)dependency.Attribute("id")).IsEqualTo(id);
		VersionRange range = VersionRange.Parse(dependency.Attribute("version")!.Value);
		await Assert.That(range.MinVersion).IsEqualTo(NuGetVersion.Parse(version));
		await Assert.That(range.IsMinInclusive).IsTrue();
		await Assert.That(range.MaxVersion).IsNull();
	}
}
