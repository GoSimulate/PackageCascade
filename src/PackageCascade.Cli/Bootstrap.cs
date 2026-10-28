using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Cli.Commands;
using PackageCascade.Public.DependencyInjection;

namespace PackageCascade.Cli;

public class Bootstrap
{
	public IServiceProvider Up()
	{
		ServiceCollection services = new();
		services.AddPackageCascade();

		RegisterCommands(services);
		ConfigureServices(services);

		return services.BuildServiceProvider();
	}

	protected virtual void ConfigureServices(IServiceCollection services)
	{
	}

	private void RegisterCommands(ServiceCollection services)
	{
		IEnumerable<Type> commandTypes = typeof(Bootstrap).Assembly.GetTypes()
			.Where(t => !t.IsAbstract && typeof(Command).IsAssignableFrom(t) && !typeof(System.CommandLine.RootCommand).IsAssignableFrom(t));

		foreach (Type type in commandTypes)
		{
			services.AddTransient(typeof(Command), type);
		}

		services.AddTransient<Commands.RootCommand>();
	}
}
