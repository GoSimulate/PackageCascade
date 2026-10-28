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
		_explicitProject = WriteProject("Explicit", "<PackageId>Custom.Package</PackageId>", "../Outside/Outside.csproj");
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

	private string WriteProject(string name, string properties, string? reference = null)
	{
		string projectDirectory = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
		string projectPath = Path.Combine(projectDirectory, name + ".csproj");
		string referenceItem = reference is null ? "" : $"<ItemGroup><ProjectReference Include=\"{reference}\" /></ItemGroup>";
		File.WriteAllText(projectPath, $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net11.0</TargetFramework>
			    {properties}
			  </PropertyGroup>
			  {referenceItem}
			</Project>
			""");
		return projectPath;
	}
}
