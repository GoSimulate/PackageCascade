using PackageCascade.Internal.Planning.Git;
using PackageCascade.Internal.Planning.SemVer;
using PackageCascade.Public.Planning;
using PackageCascade.Internal.Planning;

namespace PackageCascade.Tests.Unit.Internal.Planning.SemVer;

public class DirectoryBumpCalculatorTests
{
	[Test]
	[Arguments("fix: repair", "Patch")]
	[Arguments("perf(core): speed up", "Patch")]
	[Arguments("feat: add feature", "Minor")]
	[Arguments("feat(core)!: change API", "Major")]
	[Arguments("chore: adjust\n\nBREAKING CHANGE: change API", "Major")]
	[Arguments("docs: clarify", "None")]
	public async Task MapsConventionalCommitToBump(string message, string expected)
	{
		GitCommitDetails commit = new("commit", message, ["src/A/file.txt"]);

		Dictionary<string, SemverBump> bumps = new DirectoryBumpCalculator().Calculate([commit], ["src/A"]);

		await Assert.That(bumps["src/A"].ToString()).IsEqualTo(expected);
	}

	[Test]
	public async Task UsesStrongestRelevantBump()
	{
		GitCommitDetails[] commits =
		[
			new("first", "feat: add feature", ["src/A/feature.cs"]),
			new("second", "fix: repair", ["src/A/fix.cs"]),
		];

		Dictionary<string, SemverBump> bumps = new DirectoryBumpCalculator().Calculate(commits, ["src/A"]);

		await Assert.That(bumps["src/A"]).IsEqualTo(SemverBump.Minor);
	}

	[Test]
	public async Task DoesNotMatchSiblingDirectory()
	{
		GitCommitDetails commit = new("commit", "feat: add feature", ["src/AB/file.cs"]);

		Dictionary<string, SemverBump> bumps = new DirectoryBumpCalculator().Calculate([commit], ["src/A"]);

		await Assert.That(bumps["src/A"]).IsEqualTo(SemverBump.None);
	}

	[Test]
	public async Task MatchesProjectAtRepositoryRoot()
	{
		GitCommitDetails commit = new("commit", "fix: repair", ["Api.cs"]);

		Dictionary<string, SemverBump> bumps = new DirectoryBumpCalculator().Calculate([commit], ["."]);

		await Assert.That(bumps["."]).IsEqualTo(SemverBump.Patch);
	}

	[Test]
	public async Task AcceptsPlatformDirectorySeparators()
	{
		GitCommitDetails commit = new("commit", "fix: repair", ["src/A/file.cs"]);

		Dictionary<string, SemverBump> bumps = new DirectoryBumpCalculator().Calculate([commit], [@"src\A"]);

		await Assert.That(bumps[@"src\A"]).IsEqualTo(SemverBump.Patch);
	}

	[Test]
	public async Task ReturnsBumpForEachSuppliedDirectory()
	{
		GitCommitDetails[] commits =
		[
			new("first", "feat: add A", ["src/A/file.cs"]),
			new("second", "fix: repair B", ["src/B/file.cs"]),
		];

		Dictionary<string, SemverBump> bumps = new DirectoryBumpCalculator().Calculate(commits, ["src/A", "src/B", "src/C"]);

		await Assert.That(bumps["src/A"]).IsEqualTo(SemverBump.Minor);
		await Assert.That(bumps["src/B"]).IsEqualTo(SemverBump.Patch);
		await Assert.That(bumps["src/C"]).IsEqualTo(SemverBump.None);
	}
}
