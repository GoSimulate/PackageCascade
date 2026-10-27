using System.Diagnostics;
using PackageCascade.Cli;

namespace PackageCascade.Tests.Integration;

public class CliHelpTests
{
	[Test]
	public async Task HelpDisplaysOutput()
	{
		ProcessStartInfo startInfo = new("dotnet")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		startInfo.ArgumentList.Add(typeof(Bootstrap).Assembly.Location);
		startInfo.ArgumentList.Add("--help");

		using Process process = Process.Start(startInfo)!;
		Task<string> output = process.StandardOutput.ReadToEndAsync();
		Task<string> error = process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();
		await Task.WhenAll(output, error);

		await Assert.That(process.ExitCode).IsEqualTo(0);
		await Assert.That(await output).IsNotEmpty();
	}
}
