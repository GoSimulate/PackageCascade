using LibGit2Sharp;
using PackageCascade.Internal.Executing.Git;
using PackageCascade.Public.Definition;

namespace PackageCascade.Tests.Unit.Internal.Executing.Git;

public sealed class PackageTagWriterTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-tag-writer-").FullName;
	private readonly Repository _repository;
	private readonly PackageTagWriter _writer = new();

	public PackageTagWriterTests()
	{
		Repository.Init(_directory);
		_repository = new(_directory);
	}

	[Test]
	public async Task CreatesLightweightTagsAtPlanCommitAfterHeadMoves()
	{
		Commit target = Commit("target");
		Commit("new head");

		IReadOnlyList<string> created = _writer.Create(_directory, Plan(target, ("Acme.Core", "1.2.3"), ("Acme.Tools", "2.0.0")));

		using (Assert.Multiple())
		{
			await Assert.That(created).IsEquivalentTo(new[] { "Acme.Core-v1.2.3", "Acme.Tools-v2.0.0" });
			await Assert.That(_repository.Tags["Acme.Core-v1.2.3"].Target).IsTypeOf<Commit>();
			await Assert.That(_repository.Tags["Acme.Core-v1.2.3"].PeeledTarget.Id).IsEqualTo(target.Id);
			await Assert.That(_repository.Tags["Acme.Tools-v2.0.0"].PeeledTarget.Id).IsEqualTo(target.Id);
		}
	}

	[Test]
	public async Task RepeatedCreateThrowsAndKeepsFirstTag()
	{
		Commit target = Commit("target");
		ReleasePlan plan = Plan(target, ("Acme.Core", "1.0.0"));
		_writer.Create(_directory, plan);

		Assert.Throws(typeof(LibGit2SharpException), () => _writer.Create(_directory, plan));

		await Assert.That(_repository.Tags["Acme.Core-v1.0.0"].PeeledTarget.Id).IsEqualTo(target.Id);
	}

	[Test]
	public async Task ConflictingSecondTagLeavesFirstTag()
	{
		Commit other = Commit("other");
		Commit target = Commit("target");
		_repository.Tags.Add("Acme.Tools-v2.0.0", other);

		Assert.Throws(typeof(LibGit2SharpException), () => _writer.Create(_directory,
			Plan(target, ("Acme.Core", "1.0.0"), ("Acme.Tools", "2.0.0"))));

		using (Assert.Multiple())
		{
			await Assert.That(_repository.Tags["Acme.Core-v1.0.0"].PeeledTarget.Id).IsEqualTo(target.Id);
			await Assert.That(_repository.Tags["Acme.Tools-v2.0.0"].PeeledTarget.Id).IsEqualTo(other.Id);
		}
	}

	[Test]
	public async Task InvalidSecondTagLeavesFirstTag()
	{
		Commit target = Commit("target");

		Assert.Throws(typeof(LibGit2SharpException), () => _writer.Create(_directory,
			Plan(target, ("Acme.Core", "1.0.0"), ("Bad..Name", "2.0.0"))));

		await Assert.That(_repository.Tags["Acme.Core-v1.0.0"].PeeledTarget.Id).IsEqualTo(target.Id);
	}

	[Test]
	public async Task TagWriteFailureLeavesEarlierTag()
	{
		Commit target = Commit("target");
		string lockPath = Path.Combine(_repository.Info.Path, "refs", "tags", "Acme.Tools-v2.0.0.lock");
		File.WriteAllText(lockPath, "held");

		Assert.Throws(typeof(LibGit2SharpException), () => _writer.Create(_directory,
			Plan(target, ("Acme.Core", "1.0.0"), ("Acme.Tools", "2.0.0"))));

		using (Assert.Multiple())
		{
			await Assert.That(_repository.Tags["Acme.Core-v1.0.0"].PeeledTarget.Id).IsEqualTo(target.Id);
			await Assert.That(_repository.Tags["Acme.Tools-v2.0.0"]).IsNull();
		}
	}

	public void Dispose()
	{
		_repository.Dispose();
		Directory.Delete(_directory, recursive: true);
	}

	private Commit Commit(string contents)
	{
		File.WriteAllText(Path.Combine(_directory, "file.txt"), contents);
		Commands.Stage(_repository, "file.txt");
		Signature author = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		return _repository.Commit(contents, author, author);
	}

	private static ReleasePlan Plan(Commit target, params (string PackageId, string Version)[] packages)
		=> new(packages.Select(package => new PlannedRelease(package.PackageId, $"{package.PackageId}.csproj", package.Version, [], [])).ToArray(), target.Sha, "Release", [], ".");
}
