using LibGit2Sharp;
using PackageCascade.Internal.Planning;

namespace PackageCascade.Tests.Unit;

public sealed class GitCommitReaderTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-git-").FullName;
	private readonly Repository _repository;

	public GitCommitReaderTests()
	{
		Repository.Init(_directory);
		_repository = new Repository(_directory);
	}

	[Test]
	public async Task ReadsCommitsAfterBaselineThroughHead()
	{
		Commit baseline = Commit("chore: baseline", "src/A/file.txt", "first");
		Commit first = Commit("fix: change A", "src/A/file.txt", "second");
		Commit second = Commit("feat: add B", "src/B/file.txt", "third");

		IReadOnlyList<GitCommitDetails> commits = new GitCommitReader().ReadSince(_directory, baseline.Sha);

		await Assert.That(commits.Select(commit => commit.Sha).SequenceEqual([first.Sha, second.Sha])).IsTrue();
	}

	[Test]
	public async Task IncludesChangedPathsAndFullMessages()
	{
		Commit baseline = Commit("chore: baseline", "src/A/file.txt", "first");
		Commit("feat: change A\n\nBREAKING CHANGE: API changed", "src/A/file.txt", "second");

		GitCommitDetails commit = new GitCommitReader().ReadSince(_directory, baseline.Sha).Single();

		await Assert.That(commit.Message).IsEqualTo("feat: change A\n\nBREAKING CHANGE: API changed");
		await Assert.That(commit.ChangedPaths.Single()).IsEqualTo("src/A/file.txt");
	}

	[Test]
	public async Task ReturnsEmptyListWhenBaselineIsHead()
	{
		Commit baseline = Commit("chore: baseline", "src/A/file.txt", "first");

		IReadOnlyList<GitCommitDetails> commits = new GitCommitReader().ReadSince(_directory, baseline.Sha);

		await Assert.That(commits).IsEmpty();
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
