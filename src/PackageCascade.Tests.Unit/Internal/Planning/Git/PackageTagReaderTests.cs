using PackageCascade.Internal.Planning.Git;
using LibGit2Sharp;
using PackageCascade.Internal.Planning;

namespace PackageCascade.Tests.Unit.Internal.Planning.Git;

public sealed class PackageTagReaderTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-tags-").FullName;
	private readonly Repository _repository;

	public PackageTagReaderTests()
	{
		Repository.Init(_directory);
		_repository = new(_directory);
	}

	[Test]
	public async Task FindsLatestTagForEachDistinctPackageId()
	{
		Commit first = Commit("first");
		_repository.Tags.Add("Acme.Core-v1.9.0", first);
		_repository.Tags.Add("Acme.Tools-v0.2.0", first);

		Commit second = Commit("second");
		_repository.Tags.Add("Acme.Core-v1.10.0", second);
		_repository.Tags.Add("Acme.Core.Tools-v9.0.0", second);

		IReadOnlyDictionary<string, PackageReleaseTag> tags = new PackageTagReader()
			.ReadLatest(_directory, ["Acme.Core", "Acme.Core", "Acme.Tools"]);

		using (Assert.Multiple())
		{
			await Assert.That(tags.Count).IsEqualTo(2);
			await Assert.That(tags["Acme.Core"].Name).IsEqualTo("Acme.Core-v1.10.0");
			await Assert.That(tags["Acme.Core"].CommitSha).IsEqualTo(second.Sha);
			await Assert.That(tags["Acme.Tools"].Name).IsEqualTo("Acme.Tools-v0.2.0");
		}
	}

	[Test]
	public async Task MatchesPackageTagsWithoutCaseSensitivity()
	{
		Commit commit = Commit("release");
		_repository.Tags.Add("acme.core-v1.2.0", commit);

		IReadOnlyDictionary<string, PackageReleaseTag> tags = new PackageTagReader()
			.ReadLatest(_directory, ["Acme.Core", "ACME.CORE"]);

		using (Assert.Multiple())
		{
			await Assert.That(tags.Count).IsEqualTo(1);
			await Assert.That(tags["Acme.Core"].Name).IsEqualTo("acme.core-v1.2.0");
		}
	}

	[Test]
	public async Task IgnoresTagsNotReachableFromHead()
	{
		Commit baseline = Commit("baseline");
		_repository.Tags.Add("Acme.Core-v1.0.0", baseline);

		Branch main = _repository.Head;
		Branch other = _repository.CreateBranch("other");
		Commands.Checkout(_repository, other);
		Commit offBranch = Commit("other branch");
		_repository.Tags.Add("Acme.Core-v9.0.0", offBranch);

		Commands.Checkout(_repository, main);
		Commit("main branch");

		IReadOnlyDictionary<string, PackageReleaseTag> tags = new PackageTagReader()
			.ReadLatest(_directory, ["Acme.Core"]);

		await Assert.That(tags["Acme.Core"].Name).IsEqualTo("Acme.Core-v1.0.0");
	}

	[Test]
	public async Task OrdersPrereleaseVersionsAndReadsAnnotatedTags()
	{
		Commit first = Commit("first");
		_repository.Tags.Add("Acme.Core-v1.0.0-rc.2", first);

		Commit second = Commit("second");
		Signature tagger = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		_repository.Tags.Add("Acme.Core-v1.0.0-rc.10", second, tagger, "Release candidate");

		IReadOnlyDictionary<string, PackageReleaseTag> tags = new PackageTagReader()
			.ReadLatest(_directory, ["Acme.Core"]);

		using (Assert.Multiple())
		{
			await Assert.That(tags["Acme.Core"].Name).IsEqualTo("Acme.Core-v1.0.0-rc.10");
			await Assert.That(tags["Acme.Core"].CommitSha).IsEqualTo(second.Sha);
		}
	}

	[Test]
	public async Task OmitsPackagesWithoutMatchingTags()
	{
		Commit commit = Commit("first");
		_repository.Tags.Add("Acme.Core-v1.0.0", commit);
		_repository.Tags.Add("Acme.Tools-v1.0", commit);

		IReadOnlyDictionary<string, PackageReleaseTag> tags = new PackageTagReader()
			.ReadLatest(_directory, ["Acme.Core", "Acme.Tools", "Acme.Missing"]);

		await Assert.That(tags.Keys).IsEquivalentTo(new[] { "Acme.Core" });
	}

	[Test]
	public void RejectsEquivalentVersionsOnDifferentCommits()
	{
		Commit first = Commit("first");
		_repository.Tags.Add("Acme.Core-v1.0.0+first", first);

		Commit second = Commit("second");
		_repository.Tags.Add("Acme.Core-v1.0.0+second", second);

		Assert.Throws(typeof(InvalidOperationException), () => new PackageTagReader().ReadLatest(_directory, ["Acme.Core"]));
	}

	[Test]
	public async Task IgnoresAmbiguityInOlderVersions()
	{
		Commit first = Commit("first");
		_repository.Tags.Add("Acme.Core-v1.0.0+first", first);

		Commit second = Commit("second");
		_repository.Tags.Add("Acme.Core-v1.0.0+second", second);

		Commit latest = Commit("latest");
		_repository.Tags.Add("Acme.Core-v2.0.0", latest);

		IReadOnlyDictionary<string, PackageReleaseTag> tags = new PackageTagReader()
			.ReadLatest(_directory, ["Acme.Core"]);

		await Assert.That(tags["Acme.Core"].Name).IsEqualTo("Acme.Core-v2.0.0");
	}

	public void Dispose()
	{
		_repository.Dispose();
		Directory.Delete(_directory, recursive: true);
	}

	private Commit Commit(string contents)
	{
		string path = Path.Combine(_directory, "file.txt");
		File.WriteAllText(path, contents);

		Commands.Stage(_repository, "file.txt");
		Signature author = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		return _repository.Commit(contents, author, author);
	}
}
