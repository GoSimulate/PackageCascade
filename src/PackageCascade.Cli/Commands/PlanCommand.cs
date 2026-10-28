using System.CommandLine;
using System.Text.Json;
using JetBrains.Annotations;
using PackageCascade.Cli.Serialization;
using PackageCascade.Public.Planning;
using PackageCascade.Public.Planning.Definition;

namespace PackageCascade.Cli.Commands;

[UsedImplicitly]
public sealed class PlanCommand : Command
{
	public PlanCommand(IPackageCascadePlanner planner) : base("plan", "Plan package releases for a solution")
	{
		Argument<string> solution = new("solution")
		{
			Description = "Path to the solution file",
		};

		Option<string> repository = new("--repository")
		{
			Description = "Path to the Git repository (defaults to the current directory)",
		};

		Option<string> outFile = new("--outfile")
		{
			Description = "Write the plan to a file instead of standard output",
		};

		Arguments.Add(solution);
		Options.Add(repository);
		Options.Add(outFile);

		SetAction(parseResult =>
		{
			string solutionPath = parseResult.GetRequiredValue(solution);
			string repositoryPath = parseResult.GetValue(repository) ?? Directory.GetCurrentDirectory();

			ReleasePlan plan = planner.Generate(repositoryPath, solutionPath);
			string json = JsonSerializer.Serialize(plan, PlanJsonOptions.Options);

			string? outputPath = parseResult.GetValue(outFile);
			if (outputPath is null)
			{
				parseResult.InvocationConfiguration.Output.WriteLine(json);
			}
			else
			{
				File.WriteAllText(outputPath, json);
			}
		});
	}
}
