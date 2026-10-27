using Microsoft.Extensions.DependencyInjection;

namespace PackageCascade.Public.DependencyInjection;

public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddPackageCascade()
		{
			return services;
		}
	}
}