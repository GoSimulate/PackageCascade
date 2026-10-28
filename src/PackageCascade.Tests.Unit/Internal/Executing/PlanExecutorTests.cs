using PackageCascade.Internal.Executing;
using PackageCascade.Internal.Executing.Git;
using PackageCascade.Internal.Executing.Packaging;
using PackageCascade.Internal.Planning.Git;
using PackageCascade.Public.Executing;
using PackageCascade.Public.Planning.Definition;

namespace PackageCascade.Tests.Unit.Internal.Executing;

public sealed class PlanExecutorTests : IDisposable
{
	private readonly string _repositoryPath;
	private readonly string _outputDirectory;
	private readonly StubInputResolver _resolver;
	private readonly StubPackageBuilder _builder;
	private readonly StubArchiveVerifier _verifier;
	private readonly StubTagWriter _tags;
	private readonly StubCommitReader _commits;
	private readonly ReleasePlan _plan = new([new("A", "src/A.csproj", "1.2.0", [])], "head-sha", "Release");

	public PlanExecutorTests()
	{
		_repositoryPath = Path.Combine(Path.GetTempPath(), "packagecascade-executor-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_repositoryPath);
		_outputDirectory = Path.Combine(_repositoryPath, "artifacts");
		_resolver = new StubInputResolver(new(
			new("Packages.slnx", [Path.Combine(_repositoryPath, "src/A.csproj")], new Dictionary<string, string>(), "Release", false, _outputDirectory),
			new Dictionary<string, IReadOnlyDictionary<string, string>> { ["A"] = new Dictionary<string, string> { ["B"] = "2.0.0" } }));
		_builder = new StubPackageBuilder(input => [WriteArchive(input.OutputDirectory)]);
		_verifier = new StubArchiveVerifier();
		_tags = new StubTagWriter();
		_commits = new StubCommitReader();
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
		_resolver.Result = _resolver.Result with
		{
			BuildInput = _resolver.Result.BuildInput with { IncludeSymbols = true },
		};
		await Execute(new(_outputDirectory, true), cancellation.Token);

		using (Assert.Multiple())
		{
			await Assert.That(_resolver.Plan).IsEqualTo(_plan);
			await Assert.That(_resolver.RepositoryPath).IsEqualTo(_repositoryPath);
			await Assert.That(_resolver.SolutionPath).IsEqualTo("Packages.slnx");
			await Assert.That(_resolver.OutputDirectory).IsEqualTo(_outputDirectory);
			await Assert.That(_resolver.IncludeSymbols).IsTrue();
			await Assert.That(_builder.Input! with { OutputDirectory = _outputDirectory }).IsEqualTo(_resolver.Result.BuildInput);
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

		ExecutionResult result = await CreateExecutor().Execute(_plan with { Packages = [] }, _repositoryPath, "Packages.slnx", new(_outputDirectory));

		using (Assert.Multiple())
		{
			await Assert.That(result.PackagePaths).IsEmpty();
			await Assert.That(result.CreatedTags).IsEmpty();
			await Assert.That(File.ReadAllText(existingFile)).IsEqualTo("keep this file");
			await Assert.That(_resolver.Plan).IsNotNull();
			await Assert.That(_builder.Input).IsNull();
		}
	}

	[Test]
	public async Task EmptyPlanStillRejectsInvalidInputs()
	{
		_resolver.Error = new InvalidOperationException("Invalid plan.");

		InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			CreateExecutor().Execute(_plan with { Packages = [] }, _repositoryPath, "Packages.slnx", new(_outputDirectory)));

		await Assert.That(ReferenceEquals(error, _resolver.Error)).IsTrue();
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
		=> CreateExecutor().Execute(_plan, _repositoryPath, "Packages.slnx", options ?? new(_outputDirectory), token);

	private PlanExecutor CreateExecutor() => new(_resolver, _builder, _verifier, _tags, _commits);

	private string WriteArchive(string? directory = null)
	{
		directory ??= _outputDirectory;
		Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, "A.1.2.0.nupkg");
		File.WriteAllText(path, "package contents");
		return path;
	}

	private sealed class StubInputResolver : IExecutionInputResolver
	{
		public StubInputResolver(ResolvedExecutionInput result)
		{
			Result = result;
		}
		public ResolvedExecutionInput Result { get; set; }
		public ReleasePlan? Plan { get; private set; }
		public string? RepositoryPath { get; private set; }
		public string? SolutionPath { get; private set; }
		public string? OutputDirectory { get; private set; }
		public bool IncludeSymbols { get; private set; }
		public Exception? Error { get; set; }
		public ResolvedExecutionInput Resolve(ReleasePlan plan, string repositoryPath, string solutionPath, string outputDirectory, bool includeSymbols)
		{
			Plan = plan;
			RepositoryPath = repositoryPath;
			SolutionPath = solutionPath;
			OutputDirectory = outputDirectory;
			IncludeSymbols = includeSymbols;
			if (Error is not null)
			{
				throw Error;
			}
			return Result;
		}
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

	private sealed class StubCommitReader : IGitCommitReader
	{
		public string Head { get; set; } = "head-sha";
		public string ReadHeadSha(string repositoryPath)
		{
			return Head;
		}
		public IReadOnlyList<GitCommitDetails> ReadSince(string repositoryPath, string baselineCommitHash)
			=> throw new NotSupportedException("Executing a plan must not read commit history.");
	}
}
