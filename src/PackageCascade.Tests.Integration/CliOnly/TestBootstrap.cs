using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Cli;

namespace PackageCascade.Tests.Integration.CliOnly;

public sealed class TestBootstrap(Action<IServiceCollection> configureServices) : Bootstrap
{
	protected override void ConfigureServices(IServiceCollection services) => configureServices(services);
}
