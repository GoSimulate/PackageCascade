using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Cli.Commands;

namespace PackageCascade.Cli;

public static class Program
{
	public static Task<int> Main(string[] args) => Run(args);

	public static async Task<int> Run(string[] args, Bootstrap? bootstrap = null)
	{
		IServiceProvider services = (bootstrap ?? new Bootstrap()).Up();

		try
		{
			RootCommand root = services.GetRequiredService<RootCommand>();
			return await root.Parse(args).InvokeAsync();
		}
		finally
		{
			(services as IDisposable)?.Dispose();
		}
	}
}
