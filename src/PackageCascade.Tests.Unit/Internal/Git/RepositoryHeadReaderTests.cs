using LibGit2Sharp;
using PackageCascade.Internal.Git;

namespace PackageCascade.Tests.Unit.Internal.Git;

public sealed class RepositoryHeadReaderTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-head-").FullName;
	private readonly Repository _repository;

	public RepositoryHeadReaderTests()
	{
		Repository.Init(_directory);
		_repository = new(_directory);
	}

	[Test]
	public async Task ReadsHeadCommitSha()
	{
		File.WriteAllText(Path.Combine(_directory, "file.txt"), "content");
		Commands.Stage(_repository, "file.txt");
		Signature author = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		Commit commit = _repository.Commit("chore: initial", author, author);

		await Assert.That(new RepositoryHeadReader().ReadHeadSha(_directory)).IsEqualTo(commit.Sha);
	}

	public void Dispose()
	{
		_repository.Dispose();
		Directory.Delete(_directory, recursive: true);
	}
}
