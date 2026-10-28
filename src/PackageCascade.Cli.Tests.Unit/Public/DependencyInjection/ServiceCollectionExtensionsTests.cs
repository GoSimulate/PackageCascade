using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Public.DependencyInjection;
using PackageCascade.Public.Planning;

namespace PackageCascade.Cli.Tests.Unit.Public.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
	[Test]
	public async Task AddPackageCascadeResolvesPlanner()
	{
		using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();

		await Assert.That(provider.GetRequiredService<IPackageCascadePlanner>()).IsNotNull();
	}
}
