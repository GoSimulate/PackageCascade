using PackageCascade.Internal.Executing.Git;
using PackageCascade.Internal.Executing.Packaging;
using PackageCascade.Internal.Planning.Git;
using PackageCascade.Public.Executing;
using PackageCascade.Public.Planning.Definition;

namespace PackageCascade.Internal.Executing;

internal sealed class PlanExecutor : IPackageCascadeExecutor
{
	private readonly IExecutionInputResolver _inputResolver;
	private readonly IPackageBuilder _packageBuilder;
	private readonly IPackageArchiveVerifier _archiveVerifier;
	private readonly IPackageTagWriter _tagWriter;
	private readonly IGitCommitReader _commitReader;

	public PlanExecutor(
		IExecutionInputResolver inputResolver,
		IPackageBuilder packageBuilder,
		IPackageArchiveVerifier archiveVerifier,
		IPackageTagWriter tagWriter,
		IGitCommitReader commitReader)
	{
		_inputResolver = inputResolver;
		_packageBuilder = packageBuilder;
		_archiveVerifier = archiveVerifier;
		_tagWriter = tagWriter;
		_commitReader = commitReader;
	}

	public async Task<ExecutionResult> Execute(
		ReleasePlan plan,
		string repositoryPath,
		string solutionPath,
		ExecutionOptions options,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string repositoryRoot = Path.GetFullPath(repositoryPath);
		OutputDirectoryScope output = new(options.OutputDirectory);

		ResolvedExecutionInput resolved = _inputResolver.Resolve(plan, repositoryRoot, solutionPath,
			output.OutputPath, options.IncludeSymbols);
		if (plan.Packages.Count == 0)
		{
			return new([], []);
		}

		string stagingPath = output.CreateStagingDirectory();
		IReadOnlyList<string> archives = await _packageBuilder.Build(
			resolved.BuildInput with { OutputDirectory = stagingPath }, cancellationToken);
		ExpectedPackage[] expected = plan.Packages.Select(package => new ExpectedPackage(
			package.PackageId, package.Version, resolved.DirectDependencyVersionsByPackageId[package.PackageId])).ToArray();
		_archiveVerifier.Verify(archives, expected, options.IncludeSymbols);
		IReadOnlyList<string> packagePaths = output.CopyVerifiedArchives(archives);
		output.FinishStaging();

		EnsureHeadUnchanged(repositoryRoot, plan);
		cancellationToken.ThrowIfCancellationRequested();
		IReadOnlyList<string> createdTags = _tagWriter.Create(repositoryRoot, plan);

		cancellationToken.ThrowIfCancellationRequested();
		return new(packagePaths, createdTags);
	}

	private void EnsureHeadUnchanged(string repositoryRoot, ReleasePlan plan)
	{
		if (!_commitReader.ReadHeadSha(repositoryRoot).Equals(plan.TargetCommitSha, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Repository HEAD changed while the release plan was being applied.");
		}
	}
}
