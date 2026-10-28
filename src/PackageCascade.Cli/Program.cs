using Microsoft.Extensions.DependencyInjection;
using PackageCascade.Cli;
using PackageCascade.Cli.Commands;

Bootstrap bootstrap = new();
IServiceProvider sp = bootstrap.Up();
RootCommand root = sp.GetRequiredService<RootCommand>();

return await root.Parse(args).InvokeAsync();
