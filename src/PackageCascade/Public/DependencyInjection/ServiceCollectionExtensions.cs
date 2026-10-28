using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Internal.Planning;
using PackageCascade.Internal.Planning.Git;
using PackageCascade.Internal.Planning.Projects;
using PackageCascade.Internal.Planning.SemVer;
using PackageCascade.Public.Planning;

namespace PackageCascade.Public.DependencyInjection;

/// <summary>Registers PackageCascade services with Microsoft dependency injection.</summary>
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>Registers the release planner and its Git and MSBuild collaborators.</summary>
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
			return services;
		}
	}
}
