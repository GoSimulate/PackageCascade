using PackageCascade.Internal.Planning;

namespace PackageCascade.Tests.Unit;


public class SolutionPackageDiscovererTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-discovery-").FullName;
	private readonly string _solutionPath;
	private readonly string _defaultProject;
	private readonly string _explicitProject;
	private readonly string _nonPackableProject;
	private readonly string _outsideProject;

	public SolutionPackageDiscovererTests()
	{
		_defaultProject = WriteProject("Default", "");
		_explicitProject = WriteProject("Explicit", "<PackageId>Custom.Package</PackageId>",
			"../Default/Default.csproj", "../NotPackable/NotPackable.csproj", "../Outside/Outside.csproj");
		_nonPackableProject = WriteProject("NotPackable", "<IsPackable>false</IsPackable>");
		_outsideProject = WriteProject("Outside", "");

		_solutionPath = Path.Combine(_directory, "Packages.slnx");
		File.WriteAllText(_solutionPath, """
			<Solution>
			  <Project Path="Default/Default.csproj" />
			  <Project Path="Explicit/Explicit.csproj" />
			  <Project Path="NotPackable/NotPackable.csproj" />
			</Solution>
			""");
	}

	[Test]
	public async Task UsesDefaultPackageId()
	{
		IReadOnlyList<PackableProject> packages = new SolutionPackageDiscoverer().Discover(_solutionPath);

		await Assert.That(packages.Single(package => package.ProjectPath == _defaultProject).PackageId).IsEqualTo("Default");
	}

	[Test]
	public async Task UsesExplicitPackageId()
	{
		IReadOnlyList<PackableProject> packages = new SolutionPackageDiscoverer().Discover(_solutionPath);

		await Assert.That(packages.Single(package => package.ProjectPath == _explicitProject).PackageId).IsEqualTo("Custom.Package");
	}

	[Test]
	public async Task CapturesDirectProjectReferences()
	{
		IReadOnlyList<PackableProject> packages = new SolutionPackageDiscoverer().Discover(_solutionPath);

		await Assert.That(packages.Single(package => package.ProjectPath == _explicitProject).ReferencedProjectPaths)
			.IsEquivalentTo(new[] { _defaultProject });
	}

	[Test]
	public async Task CapturesReferencesConditionedOnTargetFramework()
	{
		File.WriteAllText(_explicitProject, """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFrameworks>net10.0;net11.0</TargetFrameworks>
			    <PackageId>Custom.Package</PackageId>
			  </PropertyGroup>
			  <ItemGroup Condition="'$(TargetFramework)' == 'net11.0'">
			    <ProjectReference Include="../Default/Default.csproj" />
			  </ItemGroup>
			</Project>
			""");

		IReadOnlyList<PackableProject> packages = new SolutionPackageDiscoverer().Discover(_solutionPath);

		await Assert.That(packages.Single(package => package.ProjectPath == _explicitProject).ReferencedProjectPaths)
			.IsEquivalentTo(new[] { _defaultProject });
	}

	[Test]
	public async Task ExcludesNonPackableProject()
	{
		IReadOnlyList<PackableProject> packages = new SolutionPackageDiscoverer().Discover(_solutionPath);

		await Assert.That(packages.Any(package => package.ProjectPath == _nonPackableProject)).IsFalse();
	}

	[Test]
	public async Task ExcludesReferencedProjectOutsideSolution()
	{
		IReadOnlyList<PackableProject> packages = new SolutionPackageDiscoverer().Discover(_solutionPath);

		await Assert.That(packages.Any(package => package.ProjectPath == _outsideProject)).IsFalse();
	}

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	private string WriteProject(string name, string properties, params string[] references)
	{
		string projectDirectory = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
		string projectPath = Path.Combine(projectDirectory, name + ".csproj");
		string referenceItems = string.Join("", references.Select(reference => $"<ProjectReference Include=\"{reference}\" />"));
		File.WriteAllText(projectPath, $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net11.0</TargetFramework>
			    {properties}
			  </PropertyGroup>
			  <ItemGroup>{referenceItems}</ItemGroup>
			</Project>
			""");
		return projectPath;
	}
}
