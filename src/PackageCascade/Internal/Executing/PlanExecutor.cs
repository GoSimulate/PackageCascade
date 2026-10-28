using PackageCascade.Internal.Executing.Git;
using PackageCascade.Internal.Executing.Packaging;
using PackageCascade.Public.Executing;
using PackageCascade.Public.Definition;
using NuGet.Versioning;

namespace PackageCascade.Internal.Executing;

internal sealed class PlanExecutor : IPackageCascadeExecutor
{
	private readonly IPackageBuilder _packageBuilder;
	private readonly IPackageArchiveVerifier _archiveVerifier;
	private readonly IPackageTagWriter _tagWriter;

	public PlanExecutor(
		IPackageBuilder packageBuilder,
		IPackageArchiveVerifier archiveVerifier,
		IPackageTagWriter tagWriter)
	{
		_packageBuilder = packageBuilder;
		_archiveVerifier = archiveVerifier;
		_tagWriter = tagWriter;
	}

	public async Task<ExecutionResult> Execute(
		ReleasePlan plan,
		string repositoryPath,
		ExecutionOptions options,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string repositoryRoot = Path.GetFullPath(repositoryPath);
		OutputDirectoryScope output = new(options.OutputDirectory);
		PackageBuildInput buildInput = CreateBuildInput(plan, repositoryRoot, output.OutputPath, options.IncludeSymbols);
		ExpectedPackage[] expected = CreateExpectedPackages(plan);
		if (plan.Packages.Count == 0)
		{
			return new([], []);
		}

		string stagingPath = output.CreateStagingDirectory();
		IReadOnlyList<string> archives = await _packageBuilder.Build(
			buildInput with { OutputDirectory = stagingPath }, cancellationToken);
		await _archiveVerifier.Verify(archives, expected, options.IncludeSymbols, cancellationToken);
		IReadOnlyList<string> packagePaths = await output.CopyVerifiedArchives(archives, cancellationToken);
		output.FinishStaging();

		cancellationToken.ThrowIfCancellationRequested();
		IReadOnlyList<string> createdTags = _tagWriter.Create(repositoryRoot, plan);

		cancellationToken.ThrowIfCancellationRequested();
		return new(packagePaths, createdTags);
	}

	private static PackageBuildInput CreateBuildInput(ReleasePlan plan, string repositoryRoot,
		string outputDirectory, bool includeSymbols)
	{
		Dictionary<string, string> versionsByPath = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		foreach ((string packageId, string projectPath, string version) in plan.Packages
			.Select(package => (package.PackageId, package.ProjectPath, package.Version))
			.Concat(plan.ReferencedPackages.Select(package => (package.PackageId, package.ProjectPath, package.Version))))
		{
			ValidateVersion(packageId, version);
			versionsByPath.Add(ResolveProjectPath(repositoryRoot, projectPath), version);
		}

		return new(Path.GetFullPath(Path.Combine(repositoryRoot, plan.WorkingDirectory)),
			plan.Packages.Select(package => ResolveProjectPath(repositoryRoot, package.ProjectPath)).ToArray(),
			versionsByPath, plan.Configuration, includeSymbols, outputDirectory);
	}

	private static ExpectedPackage[] CreateExpectedPackages(ReleasePlan plan)
	{
		Dictionary<string, string> versionsById = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string packageId, string version) in plan.Packages
			.Select(package => (package.PackageId, package.Version))
			.Concat(plan.ReferencedPackages.Select(package => (package.PackageId, package.Version))))
		{
			versionsById.Add(packageId, version);
		}

		List<ExpectedPackage> expected = [];
		foreach (PlannedRelease package in plan.Packages)
		{
			Dictionary<string, string> directDependencies = new(StringComparer.OrdinalIgnoreCase);
			foreach (string dependencyId in package.DependencyIds)
			{
				if (!versionsById.TryGetValue(dependencyId, out string? version))
				{
					throw new InvalidOperationException($"Planned dependency '{dependencyId}' of '{package.PackageId}' has no version in the release plan.");
				}

				directDependencies.Add(dependencyId, version);
			}

			expected.Add(new(package.PackageId, package.Version, directDependencies));
		}

		return expected.ToArray();
	}

	private static string ResolveProjectPath(string repositoryRoot, string projectPath)
	{
		return Path.GetFullPath(projectPath, repositoryRoot);
	}

	private static void ValidateVersion(string packageId, string version)
	{
		if (!NuGetVersion.TryParseStrict(version, out _))
		{
			throw new InvalidOperationException($"Planned package '{packageId}' has an invalid version '{version}'.");
		}
	}
}
