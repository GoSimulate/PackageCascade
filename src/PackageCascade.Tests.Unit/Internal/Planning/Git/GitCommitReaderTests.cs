using PackageCascade.Internal.Planning.Git;
using LibGit2Sharp;
using PackageCascade.Internal.Planning;

namespace PackageCascade.Tests.Unit.Internal.Planning.Git;

public sealed class GitCommitReaderTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-git-").FullName;
	private readonly Repository _repository;

	public GitCommitReaderTests()
	{
		Repository.Init(_directory);
		_repository = new(_directory);
	}

	[Test]
	public async Task ReadsCommitsAfterBaselineThroughHead()
	{
		Commit baseline = Commit("chore: baseline", "src/A/file.txt", "first");
		Commit first = Commit("fix: change A", "src/A/file.txt", "second");
		Commit second = Commit("feat: add B", "src/B/file.txt", "third");

		IReadOnlyList<GitCommitDetails> commits = new GitCommitReader().ReadSince(_directory, baseline.Sha, "src");

		await Assert.That(commits.Select(commit => commit.Sha).SequenceEqual([first.Sha, second.Sha])).IsTrue();
	}

	[Test]
	public async Task IncludesChangedPathsAndFullMessages()
	{
		Commit baseline = Commit("chore: baseline", "src/A/file.txt", "first");
		Commit("feat: change A\n\nBREAKING CHANGE: API changed", "src/A/file.txt", "second");

		GitCommitDetails commit = new GitCommitReader().ReadSince(_directory, baseline.Sha, "src").Single();

		using (Assert.Multiple())
		{
			await Assert.That(commit.Message).IsEqualTo("feat: change A\n\nBREAKING CHANGE: API changed");
			await Assert.That(commit.ChangedPaths.Single()).IsEqualTo("src/A/file.txt");
		}
	}

	[Test]
	public async Task ReturnsEmptyListWhenBaselineIsHead()
	{
		Commit baseline = Commit("chore: baseline", "src/A/file.txt", "first");

		IReadOnlyList<GitCommitDetails> commits = new GitCommitReader().ReadSince(_directory, baseline.Sha, "src");

		await Assert.That(commits).IsEmpty();
	}

	[Test]
	public async Task TracksEffectiveGlobalJsonBeforeAndAfterEachCommit()
	{
		Commit baseline = Commit("chore: baseline", "global.json", "root");
		Commit("build!: change root SDK", "global.json", "updated root");
		Commit("chore: add nested SDK", "solutions/nested/global.json", "nested");
		Commit("build: change shadowed root SDK", "global.json", "shadowed root");
		File.Delete(Path.Combine(_directory, "solutions", "nested", "global.json"));
		Commands.Stage(_repository, "solutions/nested/global.json");
		Signature author = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		_repository.Commit("chore: remove nested SDK", author, author);

		IReadOnlyList<GitCommitDetails> commits = new GitCommitReader().ReadSince(_directory, baseline.Sha, "solutions/nested");

		await Assert.That(commits.Select(commit => commit.ChangesEffectiveGlobalJson)
			.SequenceEqual([true, true, false, true])).IsTrue();
	}

	public void Dispose()
	{
		_repository.Dispose();
		Directory.Delete(_directory, recursive: true);
	}

	private Commit Commit(string message, string path, string contents)
	{
		string fullPath = Path.Combine(_directory, path);
		Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
		File.WriteAllText(fullPath, contents);

		Commands.Stage(_repository, path);
		Signature author = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		return _repository.Commit(message, author, author);
	}
}
