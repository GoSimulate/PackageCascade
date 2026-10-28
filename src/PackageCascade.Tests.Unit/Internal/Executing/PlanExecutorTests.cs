using PackageCascade.Internal.Executing;
using PackageCascade.Internal.Executing.Git;
using PackageCascade.Internal.Executing.Packaging;
using PackageCascade.Internal.Git;
using PackageCascade.Public.Executing;
using PackageCascade.Public.Definition;

namespace PackageCascade.Tests.Unit.Internal.Executing;

public sealed class PlanExecutorTests : IDisposable
{
	private readonly string _repositoryPath;
	private readonly string _outputDirectory;
	private readonly StubPackageBuilder _builder;
	private readonly StubArchiveVerifier _verifier;
	private readonly StubTagWriter _tags;
	private readonly StubHeadReader _commits;
	private readonly ReleasePlan _plan = new([new("A", "src/A.csproj", "1.2.0", [], ["B"])], "head-sha", "Release", [new("B", "src/B.csproj", "2.0.0")], ".");

	public PlanExecutorTests()
	{
		_repositoryPath = Path.Combine(Path.GetTempPath(), "packagecascade-executor-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_repositoryPath);
		_outputDirectory = Path.Combine(_repositoryPath, "artifacts");
		_builder = new StubPackageBuilder(input => [WriteArchive(input.OutputDirectory)]);
		_verifier = new StubArchiveVerifier();
		_tags = new StubTagWriter();
		_commits = new StubHeadReader();
	}

	public void Dispose()
	{
		if (_builder.Input is { OutputDirectory: string stagingPath } && Directory.Exists(stagingPath))
		{
			Directory.Delete(stagingPath, recursive: true);
		}

		if (Directory.Exists(_repositoryPath))
		{
			Directory.Delete(_repositoryPath, recursive: true);
		}
	}

	[Test]
	public async Task ForwardsPlanAndOptionsToCollaborators()
	{
		using CancellationTokenSource cancellation = new();
		await Execute(new(_outputDirectory, true), cancellation.Token);

		using (Assert.Multiple())
		{
			await Assert.That(_builder.Input!.WorkingDirectory).IsEqualTo(_repositoryPath);
			await Assert.That(_builder.Input.ProjectPaths).IsEquivalentTo(new[] { Path.Combine(_repositoryPath, "src/A.csproj") });
			await Assert.That(_builder.Input.VersionsByProjectPath[Path.Combine(_repositoryPath, "src/A.csproj")]).IsEqualTo("1.2.0");
			await Assert.That(_builder.Input.VersionsByProjectPath[Path.Combine(_repositoryPath, "src/B.csproj")]).IsEqualTo("2.0.0");
			await Assert.That(_builder.Input.Configuration).IsEqualTo("Release");
			await Assert.That(_builder.Input.IncludeSymbols).IsTrue();
			await Assert.That(_builder.Token).IsEqualTo(cancellation.Token);
			await Assert.That(_verifier.Archives).IsEquivalentTo(new[] { Path.Combine(_builder.Input!.OutputDirectory, "A.1.2.0.nupkg") });
			await Assert.That(_verifier.IncludeSymbols).IsTrue();
			await Assert.That(_verifier.Packages.Single().PackageId).IsEqualTo("A");
			await Assert.That(_verifier.Packages.Single().Version).IsEqualTo("1.2.0");
			await Assert.That(_verifier.Packages.Single().DirectDependencyVersions["B"]).IsEqualTo("2.0.0");
			await Assert.That(_tags.RepositoryPath).IsEqualTo(_repositoryPath);
			await Assert.That(_tags.Plan).IsEqualTo(_plan);
		}
	}

	[Test]
	public async Task ReturnsVerifiedArchivesAndCreatedTagsWithoutLeavingStaging()
	{
		ExecutionResult result = await Execute(new(_outputDirectory));

		using (Assert.Multiple())
		{
			await Assert.That(result.CreatedTags).IsEquivalentTo(new[] { "A-v1.2.0" });
			await Assert.That(result.PackagePaths).IsEquivalentTo(new[] { Path.Combine(_outputDirectory, "A.1.2.0.nupkg") });
			await Assert.That(File.ReadAllText(result.PackagePaths.Single())).IsEqualTo("package contents");
			await Assert.That(_builder.Input!.OutputDirectory).IsNotEqualTo(_outputDirectory);
			await Assert.That(Directory.Exists(_builder.Input.OutputDirectory)).IsFalse();
		}
	}

	[Test]
	public async Task EmptyPlanValidatesInputsWithoutChangingOutput()
	{
		Directory.CreateDirectory(_outputDirectory);
		string existingFile = Path.Combine(_outputDirectory, "existing.txt");
		File.WriteAllText(existingFile, "keep this file");

		ExecutionResult result = await CreateExecutor().Execute(_plan with { Packages = [] }, _repositoryPath, new(_outputDirectory));

		using (Assert.Multiple())
		{
			await Assert.That(result.PackagePaths).IsEmpty();
			await Assert.That(result.CreatedTags).IsEmpty();
			await Assert.That(File.ReadAllText(existingFile)).IsEqualTo("keep this file");
			await Assert.That(_commits.ReadCount).IsEqualTo(1);
			await Assert.That(_builder.Input).IsNull();
		}
	}

	[Test]
	public async Task EmptyPlanStillRejectsInvalidInputs()
	{
		ReleasePlan invalid = _plan with { Packages = [], ReferencedPackages = [new("B", "../outside.csproj", "2.0.0")] };

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			CreateExecutor().Execute(invalid, _repositoryPath, new(_outputDirectory)));
	}

	[Test]
	public async Task UsesPlannedVersionsForSelectedDependencies()
	{
		ReleasePlan plan = _plan with
		{
			Packages = [new("B", "src/B.csproj", "2.1.0", [], []), _plan.Packages.Single()],
			ReferencedPackages = [],
		};

		await CreateExecutor().Execute(plan, _repositoryPath, new(_outputDirectory));

		using (Assert.Multiple())
		{
			await Assert.That(_builder.Input!.ProjectPaths.SequenceEqual([
				Path.Combine(_repositoryPath, "src/B.csproj"), Path.Combine(_repositoryPath, "src/A.csproj")])).IsTrue();
			await Assert.That(_builder.Input.VersionsByProjectPath[Path.Combine(_repositoryPath, "src/B.csproj")]).IsEqualTo("2.1.0");
			await Assert.That(_verifier.Packages.Single(package => package.PackageId == "A").DirectDependencyVersions["B"]).IsEqualTo("2.1.0");
		}
	}

	[Test]
	public async Task UsesWorkingDirectoryFromPlan()
	{
		ReleasePlan plan = _plan with { WorkingDirectory = "solutions/nested" };

		await CreateExecutor().Execute(plan, _repositoryPath, new(_outputDirectory));

		await Assert.That(_builder.Input!.WorkingDirectory).IsEqualTo(Path.Combine(_repositoryPath, "solutions", "nested"));
	}

	[Test]
	public async Task RejectsDependencyMissingFromPlanBeforeBuilding()
	{
		ReleasePlan plan = _plan with { ReferencedPackages = [] };

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			CreateExecutor().Execute(plan, _repositoryPath, new(_outputDirectory)));

		await Assert.That(_builder.Input).IsNull();
	}

	[Test]
	public async Task ExistingOutputFilesSurviveSuccessfulExecution()
	{
		Directory.CreateDirectory(_outputDirectory);
		string existingFile = Path.Combine(_outputDirectory, "notes.txt");
		File.WriteAllText(existingFile, "user content");

		ExecutionResult result = await Execute();

		using (Assert.Multiple())
		{
			await Assert.That(File.ReadAllText(existingFile)).IsEqualTo("user content");
			await Assert.That(File.Exists(result.PackagePaths.Single())).IsTrue();
			await Assert.That(_tags.Plan).IsEqualTo(_plan);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task BuildFailureLeavesPartialStagingAndPreservesExistingFiles(bool outputAlreadyExists)
	{
		string? existingFile = null;
		if (outputAlreadyExists)
		{
			Directory.CreateDirectory(_outputDirectory);
			existingFile = Path.Combine(_outputDirectory, "notes.txt");
			File.WriteAllText(existingFile, "user content");
		}

		InvalidOperationException failure = new("Build failed.");
		_builder.BuildAction = input =>
		{
			WriteArchive(input.OutputDirectory);
			throw failure;
		};

		InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(() => Execute());

		using (Assert.Multiple())
		{
			await Assert.That(error).IsEqualTo(failure);
			await Assert.That(Directory.Exists(_outputDirectory)).IsEqualTo(outputAlreadyExists);
			if (existingFile is not null)
			{
				await Assert.That(File.ReadAllText(existingFile)).IsEqualTo("user content");
			}

			await Assert.That(File.Exists(Path.Combine(_builder.Input!.OutputDirectory, "A.1.2.0.nupkg"))).IsTrue();
			await Assert.That(File.Exists(Path.Combine(_outputDirectory, "A.1.2.0.nupkg"))).IsFalse();
		}
	}

	[Test]
	public async Task ExistingArchiveNameCannotBeOverwritten()
	{
		Directory.CreateDirectory(_outputDirectory);
		string existingArchive = Path.Combine(_outputDirectory, "A.1.2.0.snupkg");
		File.WriteAllText(existingArchive, "pre-existing symbol content");
		_builder.BuildAction = input =>
		{
			string package = WriteArchive(input.OutputDirectory);
			string symbols = Path.Combine(input.OutputDirectory, "A.1.2.0.snupkg");
			File.WriteAllText(symbols, "new symbol content");
			return [package, symbols];
		};

		await Assert.ThrowsAsync<IOException>(() => Execute(new(_outputDirectory, true)));

		using (Assert.Multiple())
		{
			await Assert.That(File.ReadAllText(existingArchive)).IsEqualTo("pre-existing symbol content");
			await Assert.That(File.Exists(Path.Combine(_outputDirectory, "A.1.2.0.nupkg"))).IsTrue();
			await Assert.That(_tags.Plan).IsNull();
			await Assert.That(Directory.Exists(_builder.Input!.OutputDirectory)).IsTrue();
		}
	}

	[Test]
	public async Task ArchiveVerificationFailurePreservesExistingFilesAndPreventsTags()
	{
		Directory.CreateDirectory(_outputDirectory);
		string existingFile = Path.Combine(_outputDirectory, "notes.txt");
		File.WriteAllText(existingFile, "user content");
		_verifier.Error = new InvalidOperationException("Archive identity mismatch.");

		InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(() => Execute());

		using (Assert.Multiple())
		{
			await Assert.That(ReferenceEquals(error, _verifier.Error)).IsTrue();
			await Assert.That(File.ReadAllText(existingFile)).IsEqualTo("user content");
			await Assert.That(Directory.Exists(_outputDirectory)).IsTrue();
			await Assert.That(Directory.Exists(_builder.Input!.OutputDirectory)).IsTrue();
			await Assert.That(_tags.Plan).IsNull();
		}
	}

	[Test]
	public async Task ChangedHeadAfterBuildPreventsTagsAndLeavesVerifiedOutput()
	{
		_commits.Head = "changed-head";

		await Assert.ThrowsAsync<InvalidOperationException>(() => Execute());

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(Path.Combine(_outputDirectory, "A.1.2.0.nupkg"))).IsTrue();
			await Assert.That(_tags.Plan).IsNull();
		}
	}

	[Test]
	public async Task UnrelatedRepositoryFilesCreatedDuringBuildDoNotBlockExecution()
	{
		string sourcePath = Path.Combine(_repositoryPath, "source.cs");
		string notesPath = Path.Combine(_repositoryPath, "notes.txt");
		_builder.BuildAction = input =>
		{
			File.WriteAllText(sourcePath, "user change");
			File.WriteAllText(notesPath, "untracked content");
			return [WriteArchive(input.OutputDirectory)];
		};

		ExecutionResult result = await Execute();

		using (Assert.Multiple())
		{
			await Assert.That(File.ReadAllText(sourcePath)).IsEqualTo("user change");
			await Assert.That(File.ReadAllText(notesPath)).IsEqualTo("untracked content");
			await Assert.That(result.PackagePaths).IsEquivalentTo(new[] { Path.Combine(_outputDirectory, "A.1.2.0.nupkg") });
			await Assert.That(_tags.Plan).IsEqualTo(_plan);
		}
	}

	[Test]
	public async Task CancellationAfterTagsLeavesTagsAndVerifiedOutput()
	{
		using CancellationTokenSource cancellation = new();
		_tags.AfterCreate = cancellation.Cancel;

		OperationCanceledException? error = await Assert.ThrowsAsync<OperationCanceledException>(() => Execute(token: cancellation.Token));

		using (Assert.Multiple())
		{
			await Assert.That(error!.CancellationToken).IsEqualTo(cancellation.Token);
			await Assert.That(_tags.Plan).IsEqualTo(_plan);
			await Assert.That(File.Exists(Path.Combine(_outputDirectory, "A.1.2.0.nupkg"))).IsTrue();
		}
	}

	[Test]
	public async Task TagCreationFailureLeavesVerifiedOutput()
	{
		IOException failure = new("Cannot create tag.");
		_tags.CreateError = failure;

		IOException? error = await Assert.ThrowsAsync<IOException>(() => Execute());

		using (Assert.Multiple())
		{
			await Assert.That(error).IsEqualTo(failure);
			await Assert.That(_tags.Plan).IsEqualTo(_plan);
			await Assert.That(File.Exists(Path.Combine(_outputDirectory, "A.1.2.0.nupkg"))).IsTrue();
		}
	}

	private Task<ExecutionResult> Execute(ExecutionOptions? options = null, CancellationToken token = default)
		=> CreateExecutor().Execute(_plan, _repositoryPath, options ?? new(_outputDirectory), token);

	private PlanExecutor CreateExecutor() => new(_builder, _verifier, _tags, _commits);

	private string WriteArchive(string? directory = null)
	{
		directory ??= _outputDirectory;
		Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, "A.1.2.0.nupkg");
		File.WriteAllText(path, "package contents");
		return path;
	}

	private sealed class StubPackageBuilder : IPackageBuilder
	{
		public StubPackageBuilder(Func<PackageBuildInput, IReadOnlyList<string>> buildAction)
		{
			BuildAction = buildAction;
		}
		public Func<PackageBuildInput, IReadOnlyList<string>> BuildAction { get; set; }
		public PackageBuildInput? Input { get; private set; }
		public CancellationToken Token { get; private set; }
		public Task<IReadOnlyList<string>> Build(PackageBuildInput input, CancellationToken cancellationToken)
		{
			Input = input;
			Token = cancellationToken;
			return Task.FromResult(BuildAction(input));
		}
	}

	private sealed class StubArchiveVerifier : IPackageArchiveVerifier
	{
		public IReadOnlyList<string> Archives { get; private set; } = [];
		public IReadOnlyList<ExpectedPackage> Packages { get; private set; } = [];
		public bool IncludeSymbols { get; private set; }
		public Exception? Error { get; set; }
		public void Verify(IReadOnlyList<string> archivePaths, IReadOnlyList<ExpectedPackage> packages, bool includeSymbols)
		{
			Archives = archivePaths;
			Packages = packages;
			IncludeSymbols = includeSymbols;
			if (Error is not null)
			{
				throw Error;
			}
		}
	}

	private sealed class StubTagWriter : IPackageTagWriter
	{
		public string? RepositoryPath { get; private set; }
		public ReleasePlan? Plan { get; private set; }
		public Action? AfterCreate { get; set; }
		public Exception? CreateError { get; set; }
		public IReadOnlyList<string> Create(string repositoryPath, ReleasePlan plan)
		{
			RepositoryPath = repositoryPath;
			Plan = plan;
			AfterCreate?.Invoke();
			if (CreateError is not null)
			{
				throw CreateError;
			}

			return ["A-v1.2.0"];
		}
	}

	private sealed class StubHeadReader : IRepositoryHeadReader
	{
		public string Head { get; set; } = "head-sha";
		public int ReadCount { get; private set; }
		public string ReadHeadSha(string repositoryPath)
		{
			ReadCount++;
			return Head;
		}
	}
}
