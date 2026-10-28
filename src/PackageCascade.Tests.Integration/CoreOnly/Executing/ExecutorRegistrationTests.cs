using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Public.DependencyInjection;
using PackageCascade.Public.Executing;

namespace PackageCascade.Tests.Integration.CoreOnly.Executing;

public sealed class ExecutorRegistrationTests
{
	[Test]
	public async Task ResolvesExecutorFromCoreRegistrations()
	{
		await using ServiceProvider provider = new ServiceCollection().AddPackageCascade().BuildServiceProvider();

		await Assert.That(provider.GetRequiredService<IPackageCascadeExecutor>()).IsNotNull();
	}
}
