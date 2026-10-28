using System.CommandLine;
using System.Text.Json;
using JetBrains.Annotations;
using PackageCascade.Cli.Serialization;
using PackageCascade.Public.Definition;
using PackageCascade.Public.Executing;

namespace PackageCascade.Cli.Commands;

[UsedImplicitly]
public sealed class ApplyCommand : Command
{
	public ApplyCommand(IPackageCascadeExecutor executor) : base("apply", "Apply a prepared release plan locally")
	{
		Option<string> output = new("--output")
		{
			Description = "Directory for generated package archives",
			Required = true,
		};

		Option<string> inFile = new("--infile")
		{
			Description = "Read the plan from a file instead of standard input",
		};

		Option<string> repository = new("--repository")
		{
			Description = "Path to the Git repository (defaults to the current directory)",
		};

		Option<bool> includeSymbols = new("--include-symbols")
		{
			Description = "Generate symbol packages",
		};

		Options.Add(output);
		Options.Add(inFile);
		Options.Add(repository);
		Options.Add(includeSymbols);

		SetAction(async (parseResult, cancellationToken) =>
		{
			string? inputPath = parseResult.GetValue(inFile);
			string json = inputPath is null
				? await Console.In.ReadToEndAsync(cancellationToken)
				: await File.ReadAllTextAsync(inputPath, cancellationToken);
			ReleasePlan plan = JsonSerializer.Deserialize<ReleasePlan>(json, PlanJsonOptions.Options)
				?? throw new JsonException("The release plan is null.");

			string repositoryPath = parseResult.GetValue(repository) ?? Directory.GetCurrentDirectory();
			ExecutionOptions options = new(parseResult.GetRequiredValue(output), parseResult.GetValue(includeSymbols));
			ExecutionResult result = await executor.Execute(plan, repositoryPath, options, cancellationToken);
			string resultJson = JsonSerializer.Serialize(result);
			await parseResult.InvocationConfiguration.Output.WriteLineAsync(resultJson.AsMemory(), cancellationToken);
		});
	}
}
