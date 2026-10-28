using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Internal.Executing;
using PackageCascade.Internal.Executing.Git;
using PackageCascade.Internal.Executing.Packaging;
using PackageCascade.Internal.Planning;
using PackageCascade.Internal.Planning.Git;
using PackageCascade.Internal.Planning.Projects;
using PackageCascade.Internal.Planning.SemVer;
using PackageCascade.Public.Executing;
using PackageCascade.Public.Planning;

namespace PackageCascade.Public.DependencyInjection;

/// <summary>Registers PackageCascade services with Microsoft dependency injection.</summary>
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>Registers the release planner, executor and their Git and MSBuild collaborators.</summary>
		/// <returns>The service collection so registrations can be chained.</returns>
		public IServiceCollection AddPackageCascade()
		{
			services.AddTransient<ISolutionPackageDiscoverer, SolutionPackageDiscoverer>();
			services.AddTransient<IGitCommitReader, GitCommitReader>();
			services.AddTransient<IPackageTagReader, PackageTagReader>();
			services.AddTransient<IPackageDependencyGraphBuilder, PackageDependencyGraphBuilder>();
			services.AddTransient<IDirectoryBumpCalculator, DirectoryBumpCalculator>();
			services.AddTransient<IPackageBumpCalculator, PackageBumpCalculator>();
			services.AddTransient<IPackageCascadePlanner, PlanGenerator>();
			services.AddTransient<IExecutionInputResolver, ExecutionInputResolver>();
			services.AddTransient<IPackageBuilder, PackageBuilder>();
			services.AddTransient<IPackageArchiveVerifier, PackageArchiveVerifier>();
			services.AddTransient<IPackageTagWriter, PackageTagWriter>();
			services.AddTransient<IPackageCascadeExecutor, PlanExecutor>();

			return services;
		}
	}
}
