using PackageCascade.Internal.Planning.Git;
using PackageCascade.Internal.Planning.SemVer;
using PackageCascade.Public.Definition;
using PackageCascade.Internal.Planning;

namespace PackageCascade.Tests.Unit.Internal.Planning.SemVer;

public class DirectoryBumpCalculatorTests
{
	[Test]
	[Arguments("Directory.Build.props")]
	[Arguments("Directory.Build.targets")]
	[Arguments("Directory.Packages.props")]
	[Arguments("global.json")]
	[Arguments("NuGet.Config")]
	public async Task RootSharedFileChangeBumpsEveryDirectory(string path)
	{
		GitCommitDetails commit = new("commit", "build: update shared settings", [path]);

		Dictionary<string, SemverBump> bumps = new DirectoryBumpCalculator().Calculate([commit], ["src/A", "tools/B"]);

		using (Assert.Multiple())
		{
			await Assert.That(bumps["src/A"]).IsEqualTo(SemverBump.Patch);
			await Assert.That(bumps["tools/B"]).IsEqualTo(SemverBump.Patch);
		}
	}

	[Test]
	[Arguments("Update shared settings", "Patch")]
	[Arguments("fix: correct shared settings", "Patch")]
	[Arguments("feat: enable new build feature", "Minor")]
	[Arguments("build!: change shared settings", "Major")]
	public async Task RootSharedFileUsesCommitBumpWithPatchMinimum(string message, string expected)
	{
		GitCommitDetails commit = new("commit", message, ["Directory.Build.props"]);

		Dictionary<string, SemverBump> bumps = new DirectoryBumpCalculator().Calculate([commit], ["src/A", "src/B"]);

		using (Assert.Multiple())
		{
			await Assert.That(bumps["src/A"].ToString()).IsEqualTo(expected);
			await Assert.That(bumps["src/B"].ToString()).IsEqualTo(expected);
		}
	}

	[Test]
	[Arguments("src/A/Directory.Build.props")]
	[Arguments("src/A/Directory.Build.targets")]
	[Arguments("src/A/Directory.Packages.props")]
	[Arguments("src/A/global.json")]
	[Arguments("src/A/NuGet.Config")]
	public async Task NestedSharedFilenameOnlyAffectsItsOwnDirectory(string path)
	{
		GitCommitDetails commit = new("commit", "fix: correct settings", [path]);

		Dictionary<string, SemverBump> bumps = new DirectoryBumpCalculator().Calculate([commit], ["src/A", "src/B"]);

		using (Assert.Multiple())
		{
			await Assert.That(bumps["src/A"]).IsEqualTo(SemverBump.Patch);
			await Assert.That(bumps["src/B"]).IsEqualTo(SemverBump.None);
		}
	}

	[Test]
	[Arguments("README.md")]
	[Arguments(".editorconfig")]
	[Arguments("Example.slnx")]
	[Arguments("Directory.Build.props.bak")]
	public async Task OtherRootFilesDoNotBumpEveryDirectory(string path)
	{
		GitCommitDetails commit = new("commit", "fix: update repository file", [path]);

		Dictionary<string, SemverBump> bumps = new DirectoryBumpCalculator().Calculate([commit], ["src/A"]);

		await Assert.That(bumps["src/A"]).IsEqualTo(SemverBump.None);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CombinesSharedAndPackageChangesUsingStrongestBump(bool reverseOrder)
	{
		GitCommitDetails[] commits =
		[
			new("shared", "feat: enable shared feature", ["global.json"]),
			new("package", "feat!: change A API", ["src/A/Api.cs"]),
		];

		Dictionary<string, SemverBump> bumps = new DirectoryBumpCalculator().Calculate(
			reverseOrder ? commits.Reverse() : commits, ["src/A", "src/B"]);

		using (Assert.Multiple())
		{
			await Assert.That(bumps["src/A"]).IsEqualTo(SemverBump.Major);
			await Assert.That(bumps["src/B"]).IsEqualTo(SemverBump.Minor);
		}
	}

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

		using (Assert.Multiple())
		{
			await Assert.That(bumps["src/A"]).IsEqualTo(SemverBump.Minor);
			await Assert.That(bumps["src/B"]).IsEqualTo(SemverBump.Patch);
			await Assert.That(bumps["src/C"]).IsEqualTo(SemverBump.None);
		}
	}
}
