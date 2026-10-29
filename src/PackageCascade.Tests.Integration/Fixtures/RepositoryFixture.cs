using System.Xml.Linq;
using LibGit2Sharp;

namespace PackageCascade.Tests.Integration.Fixtures;

internal sealed class RepositoryFixture : IDisposable
{
	public string DirectoryPath { get; } = Directory.CreateTempSubdirectory("packagecascade-scenario-").FullName;
	public string SolutionPath => Path.Combine(DirectoryPath, "Packages.slnx");
	public string OutputPath => Path.Combine(DirectoryPath, "packages");
	public Repository Git { get; }

	public RepositoryFixture()
	{
		Repository.Init(DirectoryPath);
		Git = new(DirectoryPath);
		Write(".gitignore", "**/bin/\n**/obj/\npackages/\nplan.json\n");
		Write("global.json", """
			{"sdk":{"version":"11.0.100-rc.2.26504.105","allowPrerelease":true,"rollForward":"latestPatch"}}
			""");
		Write("NuGet.Config", "<configuration><packageSources><clear /></packageSources></configuration>");
		Write("Directory.Build.props", "<Project><PropertyGroup><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
		new XDocument(new XElement("Solution")).Save(SolutionPath);
	}

	public void AddProject(string name, string? packageId = null)
	{
		XElement properties = new("PropertyGroup",
			new XElement("TargetFramework", "net11.0"),
			new XElement("DebugType", "portable"));
		if (packageId is not null)
		{
			properties.Add(new XElement("PackageId", packageId));
		}

		Write($"src/{name}/{name}.csproj",
			new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties).ToString());
		Write($"src/{name}/Api.cs", $"public static class {name} {{ public static int Value => 42; }}\n");
		XDocument solution = XDocument.Load(SolutionPath);
		solution.Root!.Add(new XElement("Project", new XAttribute("Path", $"src/{name}/{name}.csproj")));
		solution.Save(SolutionPath);
	}

	public void AddReference(string consumer, string dependency, bool privateAssets = false, string? configuration = null)
	{
		string projectPath = Path.Combine(DirectoryPath, "src", consumer, consumer + ".csproj");
		XDocument project = XDocument.Load(projectPath);
		XElement reference = new("ProjectReference", new XAttribute("Include", $"../{dependency}/{dependency}.csproj"));
		if (privateAssets)
		{
			reference.Add(new XAttribute("PrivateAssets", "all"));
		}

		if (configuration is not null)
		{
			reference.Add(new XAttribute("Condition", $"'$(Configuration)' == '{configuration}'"));
		}

		project.Root!.Add(new XElement("ItemGroup", reference));
		project.Save(projectPath);
	}

	public void Write(string relativePath, string contents)
	{
		string path = Path.Combine(DirectoryPath, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, contents);
	}

	public Commit Commit(string message, params string[] paths)
	{
		foreach (string path in paths)
		{
			Commands.Stage(Git, path);
		}

		Signature author = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		return Git.Commit(message, author, author);
	}

	public void Tag(string packageId, string version) => Git.Tags.Add($"{packageId}-v{version}", Git.Head.Tip);

	public void Dispose()
	{
		Git.Dispose();
		Directory.Delete(DirectoryPath, recursive: true);
	}
}
