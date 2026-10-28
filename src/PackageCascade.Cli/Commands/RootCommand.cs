using System.CommandLine;

namespace PackageCascade.Cli.Commands;

public class RootCommand : System.CommandLine.RootCommand
{
	public RootCommand(IEnumerable<Command> commands) : base("PackageCascade CLI")
	{
		foreach (Command command in commands)
		{
			Add(command);
		}
	}
}
