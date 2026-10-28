using PackageCascade.Internal.Planning.Projects;
using PackageCascade.Internal.Planning;

namespace PackageCascade.Tests.Unit.Internal.Planning.Projects;

public class PackageDependencyGraphBuilderTests
{
	private readonly PackableProject _application;
	private readonly PackableProject _library;
	private readonly PackableProject _foundation;
	private readonly PackableProject _isolated;

	public PackageDependencyGraphBuilderTests()
	{
		string root = Path.Combine(Path.GetTempPath(), "packagecascade-graph-tests");
		string applicationPath = Path.Combine(root, "Application.csproj");
		string libraryPath = Path.Combine(root, "Library.csproj");
		string foundationPath = Path.Combine(root, "Foundation.csproj");
		string externalPath = Path.Combine(root, "External.csproj");

		_application = new(applicationPath, "Application", [libraryPath, externalPath]);
		_library = new(libraryPath, "Library", [foundationPath]);
		_foundation = new(foundationPath, "Foundation", []);
		_isolated = new(Path.Combine(root, "Isolated.csproj"), "Isolated", []);
	}

	[Test]
	public async Task IncludesOnlyDirectDependenciesAmongSuppliedProjects()
	{
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> graph = BuildGraph();

		using (Assert.Multiple())
		{
			await Assert.That(graph[_application]).IsEquivalentTo(new[] { _library });
			await Assert.That(graph[_library]).IsEquivalentTo(new[] { _foundation });
		}
	}

	[Test]
	public async Task IncludesProjectsWithoutDependencies()
	{
		IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> graph = BuildGraph();

		using (Assert.Multiple())
		{
			await Assert.That(graph.Count).IsEqualTo(4);
			await Assert.That(graph[_foundation]).IsEmpty();
			await Assert.That(graph[_isolated]).IsEmpty();
		}
	}

	private IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> BuildGraph()
	{
		return new PackageDependencyGraphBuilder().Build([_application, _library, _foundation, _isolated]);
	}
}
