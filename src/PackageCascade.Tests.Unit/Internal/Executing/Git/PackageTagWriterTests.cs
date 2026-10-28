using LibGit2Sharp;
using PackageCascade.Internal.Executing.Git;
using PackageCascade.Public.Planning.Definition;

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
	public async Task PreservesMatchingLightweightAndAnnotatedTagsWhileCreatingMissingTag()
	{
		Commit target = Commit("target");
		_repository.Tags.Add("Acme.Core-v1.0.0", target);
		Signature tagger = new("PackageCascade Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
		_repository.Tags.Add("Acme.Tools-v2.0.0", target, tagger, "Existing release");

		ReleasePlan plan = Plan(target, ("Acme.Core", "1.0.0"), ("Acme.Tools", "2.0.0"), ("Acme.Extra", "3.0.0"));
		IReadOnlyList<string> created = _writer.Create(_directory, plan);

		using (Assert.Multiple())
		{
			await Assert.That(created).IsEquivalentTo(new[] { "Acme.Extra-v3.0.0" });
			await Assert.That(_repository.Tags["Acme.Core-v1.0.0"]).IsNotNull();
			await Assert.That(_repository.Tags["Acme.Tools-v2.0.0"]).IsNotNull();
			await Assert.That(_repository.Tags["Acme.Extra-v3.0.0"]).IsNotNull();
		}
	}

	[Test]
	public async Task RepeatedCreateDoesNotCreateTagsAgain()
	{
		Commit target = Commit("target");
		ReleasePlan plan = Plan(target, ("Acme.Core", "1.0.0"));

		IReadOnlyList<string> first = _writer.Create(_directory, plan);
		IReadOnlyList<string> repeated = _writer.Create(_directory, plan);

		using (Assert.Multiple())
		{
			await Assert.That(first).IsEquivalentTo(new[] { "Acme.Core-v1.0.0" });
			await Assert.That(repeated).IsEmpty();
			await Assert.That(_repository.Tags["Acme.Core-v1.0.0"]).IsNotNull();
		}
	}

	[Test]
	public async Task ReusesTagWithDifferentPackageIdCasingAtSameTarget()
	{
		Commit target = Commit("target");
		_repository.Tags.Add("acme.core-v1.0.0", target);

		IReadOnlyList<string> created = _writer.Create(_directory, Plan(target, ("Acme.Core", "1.0.0")));

		using (Assert.Multiple())
		{
			await Assert.That(created).IsEmpty();
			await Assert.That(_repository.Tags["acme.core-v1.0.0"]).IsNotNull();
			await Assert.That(_repository.Tags["Acme.Core-v1.0.0"]).IsNull();
		}
	}

	[Test]
	public async Task RejectsAnyCaseVariantTagConflictBeforeCreatingTags()
	{
		Commit other = Commit("other");
		Commit target = Commit("target");
		_repository.Tags.Add("ACME.CORE-v1.0.0", target);
		_repository.Tags.Add("acme.core-v1.0.0", other);

		Assert.Throws(typeof(InvalidOperationException), () => _writer.Create(_directory,
			Plan(target, ("Acme.New", "2.0.0"), ("Acme.Core", "1.0.0"))));

		using (Assert.Multiple())
		{
			await Assert.That(_repository.Tags["Acme.New-v2.0.0"]).IsNull();
			await Assert.That(_repository.Tags["ACME.CORE-v1.0.0"]).IsNotNull();
			await Assert.That(_repository.Tags["acme.core-v1.0.0"]).IsNotNull();
		}
	}

	[Test]
	public async Task KeepsVersionSuffixComparisonExact()
	{
		Commit target = Commit("target");
		_repository.Tags.Add("ACME.CORE-v1.0.0-ALPHA", target);

		IReadOnlyList<string> created = _writer.Create(_directory, Plan(target, ("Acme.Core", "1.0.0-alpha")));

		using (Assert.Multiple())
		{
			await Assert.That(created).IsEquivalentTo(new[] { "Acme.Core-v1.0.0-alpha" });
			await Assert.That(_repository.Tags["ACME.CORE-v1.0.0-ALPHA"]).IsNotNull();
			await Assert.That(_repository.Tags["Acme.Core-v1.0.0-alpha"]).IsNotNull();
		}
	}

	[Test]
	public async Task RemovesOnlyNamedTags()
	{
		Commit target = Commit("target");
		_repository.Tags.Add("Acme.Core-v1.0.0", target);
		IReadOnlyList<string> created = _writer.Create(_directory,
			Plan(target, ("Acme.Core", "1.0.0"), ("Acme.Extra", "3.0.0")));

		_writer.Remove(_directory, created);

		using (Assert.Multiple())
		{
			await Assert.That(_repository.Tags["Acme.Core-v1.0.0"]).IsNotNull();
			await Assert.That(_repository.Tags["Acme.Extra-v3.0.0"]).IsNull();
		}
	}

	[Test]
	public async Task ConflictingSecondTagPreventsCreationOfFirst()
	{
		Commit other = Commit("other");
		Commit target = Commit("target");
		_repository.Tags.Add("Acme.Tools-v2.0.0", other);

		Assert.Throws(typeof(InvalidOperationException), () => _writer.Create(_directory, Plan(target, ("Acme.Core", "1.0.0"), ("Acme.Tools", "2.0.0"))));

		await Assert.That(_repository.Tags["Acme.Core-v1.0.0"]).IsNull();
	}

	[Test]
	public async Task InvalidSecondTagPreventsCreationOfFirst()
	{
		Commit target = Commit("target");

		Assert.Throws(typeof(InvalidOperationException), () => _writer.Create(_directory, Plan(target, ("Acme.Core", "1.0.0"), ("Bad..Name", "2.0.0"))));

		await Assert.That(_repository.Tags["Acme.Core-v1.0.0"]).IsNull();
	}

	[Test]
	public async Task RejectsExistingTagThatDoesNotPeelToCommit()
	{
		Commit target = Commit("target");
		Blob blob = _repository.ObjectDatabase.CreateBlob(new MemoryStream("contents"u8.ToArray()));
		_repository.Tags.Add("Acme.Core-v1.0.0", blob);

		Assert.Throws(typeof(InvalidOperationException), () => _writer.Create(_directory, Plan(target, ("Acme.Core", "1.0.0"))));

		await Assert.That(_repository.Tags["Acme.Core-v1.0.0"].PeeledTarget).IsTypeOf<Blob>();
	}

	[Test]
	public async Task RemovesTagsCreatedBeforeAWriteFailure()
	{
		Commit target = Commit("target");
		string lockPath = Path.Combine(_repository.Info.Path, "refs", "tags", "Acme.Tools-v2.0.0.lock");
		File.WriteAllText(lockPath, "held");

		Assert.Throws(typeof(LibGit2SharpException), () => _writer.Create(_directory, Plan(target, ("Acme.Core", "1.0.0"), ("Acme.Tools", "2.0.0"))));

		using (Assert.Multiple())
		{
			await Assert.That(_repository.Tags["Acme.Core-v1.0.0"]).IsNull();
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
		=> new(packages.Select(package => new PlannedRelease(package.PackageId, $"{package.PackageId}.csproj", package.Version, [])).ToArray(), target.Sha, "Release");
}
