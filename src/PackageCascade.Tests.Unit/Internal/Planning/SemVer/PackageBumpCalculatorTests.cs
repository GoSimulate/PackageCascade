using PackageCascade.Internal.Planning.Projects;
using PackageCascade.Internal.Planning.SemVer;
using PackageCascade.Public.Planning.Definition;
using PackageCascade.Internal.Planning;

namespace PackageCascade.Tests.Unit.Internal.Planning.SemVer;

public class PackageBumpCalculatorTests
{
	private readonly string _repositoryPath = Path.Combine(Path.GetTempPath(), "packagecascade-bump-tests");
	private readonly PackableProject _application;
	private readonly PackableProject _library;
	private readonly PackableProject _foundation;
	private readonly PackableProject _unrelated;

	public PackageBumpCalculatorTests()
	{
		_application = Project("Application");
		_library = Project("Library");
		_foundation = Project("Foundation");
		_unrelated = Project("Unrelated");
	}

	[Test]
	public async Task PatchesDependantsOfChangedPackagesTransitively()
	{
		IReadOnlyDictionary<PackableProject, SemverBump> bumps = Calculate(new Dictionary<string, SemverBump>
		{
			["src/Foundation"] = SemverBump.Major,
		});

		using (Assert.Multiple())
		{
			await Assert.That(bumps[_application]).IsEqualTo(SemverBump.Patch);
			await Assert.That(bumps[_library]).IsEqualTo(SemverBump.Patch);
			await Assert.That(bumps[_foundation]).IsEqualTo(SemverBump.Major);
			await Assert.That(bumps[_unrelated]).IsEqualTo(SemverBump.None);
		}
	}

	[Test]
	public async Task KeepsOwnStrongerBumpWhenDependencyChanges()
	{
		IReadOnlyDictionary<PackableProject, SemverBump> bumps = Calculate(new Dictionary<string, SemverBump>
		{
			["src/Application"] = SemverBump.Minor,
			["src/Library"] = SemverBump.Major,
		});

		await Assert.That(bumps[_application]).IsEqualTo(SemverBump.Minor);
	}

	[Test]
	public async Task LeavesProjectsWithoutChangesOrChangedDependenciesAlone()
	{
		IReadOnlyDictionary<PackableProject, SemverBump> bumps = Calculate(new Dictionary<string, SemverBump>());

		await Assert.That(bumps.Values).IsEquivalentTo(new[]
		{
			SemverBump.None, SemverBump.None, SemverBump.None, SemverBump.None,
		});
	}

	private IReadOnlyDictionary<PackableProject, SemverBump> Calculate(IReadOnlyDictionary<string, SemverBump> changedBumps)
	{
		Dictionary<PackableProject, IReadOnlyList<PackableProject>> graph = new()
		{
			[_application] = [_library],
			[_library] = [_foundation],
			[_foundation] = [],
			[_unrelated] = [],
		};

		Dictionary<string, SemverBump> directoryBumps = new()
		{
			["src/Application"] = SemverBump.None,
			["src/Library"] = SemverBump.None,
			["src/Foundation"] = SemverBump.None,
			["src/Unrelated"] = SemverBump.None,
		};

		foreach ((string directory, SemverBump bump) in changedBumps)
		{
			directoryBumps[directory] = bump;
		}

		return new PackageBumpCalculator().Calculate(_repositoryPath, graph, directoryBumps);
	}

	private PackableProject Project(string name)
	{
		return new(Path.Combine(_repositoryPath, "src", name, name + ".csproj"), name, []);
	}
}
