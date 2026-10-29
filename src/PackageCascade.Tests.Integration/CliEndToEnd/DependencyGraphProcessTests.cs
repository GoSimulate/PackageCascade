using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PackageCascade.Cli;
using PackageCascade.Tests.Integration.Fixtures;

namespace PackageCascade.Tests.Integration.CliEndToEnd;

public sealed class DependencyGraphProcessTests
{
	[Test]
	public async Task BuiltCliAppliesDiamondReleaseWithoutIsolatedPackage(CancellationToken cancellationToken)
	{
		using RepositoryFixture fixture = new();
		fixture.AddProject("F");
		fixture.AddProject("L");
		fixture.AddProject("R");
		fixture.AddProject("A");
		fixture.AddProject("U");
		fixture.AddReference("L", "F");
		fixture.AddReference("R", "F");
		fixture.AddReference("A", "L");
		fixture.AddReference("A", "R");
		fixture.Write("src/L/Api.cs", "public static class L { public static int Value => F.Value; }\n");
		fixture.Write("src/R/Api.cs", "public static class R { public static int Value => F.Value; }\n");
		fixture.Write("src/A/Api.cs", "public static class A { public static int Value => L.Value + R.Value; }\n");
		fixture.Commit("chore: add package graph", ".gitignore", "global.json", "NuGet.Config",
			"Directory.Build.props", "Packages.slnx", "src/F/F.csproj", "src/F/Api.cs",
			"src/L/L.csproj", "src/L/Api.cs", "src/R/R.csproj", "src/R/Api.cs",
			"src/A/A.csproj", "src/A/Api.cs", "src/U/U.csproj", "src/U/Api.cs");
		fixture.Tag("F", "1.2.3");
		fixture.Tag("L", "2.3.4");
		fixture.Tag("R", "5.6.7");
		fixture.Tag("A", "3.4.5");
		fixture.Tag("U", "4.5.6");
		fixture.Write("src/F/Api.cs", "public static class F { public static int Value => 43; }\n");
		fixture.Commit("feat!: change F API", "src/F/Api.cs");
		fixture.Write("src/L/Api.cs", "public static class L { public static int Value => F.Value + 1; }\n");
		fixture.Commit("feat: extend L API", "src/L/Api.cs");

		string planPath = Path.Combine(fixture.DirectoryPath, "plan.json");
		(int planExitCode, string planOutput, string planError) = await Run(fixture, cancellationToken,
			"plan", fixture.SolutionPath, "--repository", fixture.DirectoryPath, "--outfile", planPath);
		using (Assert.Multiple())
		{
			await Assert.That(planExitCode).IsEqualTo(0).Because(planError);
			await Assert.That(planOutput).IsEmpty();
		}
		using (JsonDocument plan = JsonDocument.Parse(await File.ReadAllTextAsync(planPath, cancellationToken)))
		{
			JsonElement[] packages = plan.RootElement.GetProperty("Packages").EnumerateArray().ToArray();
			string[] packageIds = packages.Select(package => package.GetProperty("PackageId").GetString()!).ToArray();
			using (Assert.Multiple())
			{
				await Assert.That(packages.Select(package => (
					Id: package.GetProperty("PackageId").GetString()!,
					Version: package.GetProperty("Version").GetString()!)))
					.IsEquivalentTo(new (string Id, string Version)[]
					{
						("F", "2.0.0"), ("L", "2.4.0"), ("R", "5.6.8"), ("A", "3.4.6"),
					});
				await Assert.That(Array.IndexOf(packageIds, "F") < Array.IndexOf(packageIds, "L")).IsTrue();
				await Assert.That(Array.IndexOf(packageIds, "F") < Array.IndexOf(packageIds, "R")).IsTrue();
				await Assert.That(Array.IndexOf(packageIds, "L") < Array.IndexOf(packageIds, "A")).IsTrue();
				await Assert.That(Array.IndexOf(packageIds, "R") < Array.IndexOf(packageIds, "A")).IsTrue();
				await Assert.That(plan.RootElement.GetProperty("ReferencedPackages").GetArrayLength()).IsEqualTo(0);
				await AssertDependencies(packages, "F");
				await AssertDependencies(packages, "L", "F");
				await AssertDependencies(packages, "R", "F");
				await AssertDependencies(packages, "A", "L", "R");
			}
		}

		(int applyExitCode, string applyOutput, string applyError) = await Run(fixture, cancellationToken,
			"apply", "--repository", fixture.DirectoryPath, "--infile", planPath, "--output", fixture.OutputPath);
		await Assert.That(applyExitCode).IsEqualTo(0).Because(applyError);
		string[] expectedPaths = [
			Path.Combine(fixture.OutputPath, "F.2.0.0.nupkg"),
			Path.Combine(fixture.OutputPath, "L.2.4.0.nupkg"),
			Path.Combine(fixture.OutputPath, "R.5.6.8.nupkg"),
			Path.Combine(fixture.OutputPath, "A.3.4.6.nupkg"),
		];
		string[] expectedTags = ["F-v2.0.0", "L-v2.4.0", "R-v5.6.8", "A-v3.4.6"];
		await AssertApplyResult(fixture, applyOutput, expectedPaths, expectedTags);
		using (Assert.Multiple())
		{
			await PackageArchiveAssertions.AssertArchive(expectedPaths[0], "F", "2.0.0", cancellationToken);
			await PackageArchiveAssertions.AssertArchive(expectedPaths[1], "L", "2.4.0", cancellationToken, ("F", "2.0.0"));
			await PackageArchiveAssertions.AssertArchive(expectedPaths[2], "R", "5.6.8", cancellationToken, ("F", "2.0.0"));
			await PackageArchiveAssertions.AssertArchive(expectedPaths[3], "A", "3.4.6", cancellationToken,
				("L", "2.4.0"), ("R", "5.6.8"));
			await Assert.That(fixture.Git.Tags.Select(tag => tag.FriendlyName)).IsEquivalentTo(new[]
			{
				"F-v1.2.3", "L-v2.3.4", "R-v5.6.7", "A-v3.4.5", "U-v4.5.6",
				"F-v2.0.0", "L-v2.4.0", "R-v5.6.8", "A-v3.4.6",
			});
		}
	}

	[Test]
	public async Task BuiltCliAppliesConsumerOnlyThenEmptyRelease(CancellationToken cancellationToken)
	{
		using RepositoryFixture fixture = new();
		fixture.AddProject("F");
		fixture.AddProject("L");
		fixture.AddProject("A");
		fixture.AddProject("U");
		fixture.AddReference("L", "F");
		fixture.AddReference("A", "L");
		fixture.Write("src/L/Api.cs", "public static class L { public static int Value => F.Value; }\n");
		fixture.Write("src/A/Api.cs", "public static class A { public static int Value => L.Value; }\n");
		fixture.Commit("chore: add package chain", ".gitignore", "global.json", "NuGet.Config",
			"Directory.Build.props", "Packages.slnx", "src/F/F.csproj", "src/F/Api.cs",
			"src/L/L.csproj", "src/L/Api.cs", "src/A/A.csproj", "src/A/Api.cs",
			"src/U/U.csproj", "src/U/Api.cs");
		fixture.Tag("F", "1.2.3");
		fixture.Tag("L", "2.3.4");
		fixture.Tag("A", "3.4.5");
		fixture.Tag("U", "4.5.6");
		fixture.Write("src/A/Api.cs", "public static class A { public static int Value => L.Value + 1; }\n");
		fixture.Commit("feat: extend A API", "src/A/Api.cs");

		string planPath = Path.Combine(fixture.DirectoryPath, "plan.json");
		(int planExitCode, string planOutput, string planError) = await Run(fixture, cancellationToken,
			"plan", fixture.SolutionPath, "--repository", fixture.DirectoryPath, "--outfile", planPath);
		using (Assert.Multiple())
		{
			await Assert.That(planExitCode).IsEqualTo(0).Because(planError);
			await Assert.That(planOutput).IsEmpty();
		}
		using (JsonDocument plan = JsonDocument.Parse(await File.ReadAllTextAsync(planPath, cancellationToken)))
		{
			JsonElement package = plan.RootElement.GetProperty("Packages").EnumerateArray().Single();
			JsonElement[] references = plan.RootElement.GetProperty("ReferencedPackages").EnumerateArray().ToArray();
			using (Assert.Multiple())
			{
				await Assert.That(package.GetProperty("PackageId").GetString()).IsEqualTo("A");
				await Assert.That(package.GetProperty("Version").GetString()).IsEqualTo("3.5.0");
				await Assert.That(package.GetProperty("DependencyIds").EnumerateArray().Select(id => id.GetString()!))
					.IsEquivalentTo(new[] { "L" });
				await Assert.That(references.Select(reference => (
					Id: reference.GetProperty("PackageId").GetString()!,
					Version: reference.GetProperty("Version").GetString()!)))
					.IsEquivalentTo(new (string Id, string Version)[] { ("F", "1.2.3"), ("L", "2.3.4") });
			}
		}

		(int applyExitCode, string applyOutput, string applyError) = await Run(fixture, cancellationToken,
			"apply", "--repository", fixture.DirectoryPath, "--infile", planPath, "--output", fixture.OutputPath);
		await Assert.That(applyExitCode).IsEqualTo(0).Because(applyError);
		string archivePath = Path.Combine(fixture.OutputPath, "A.3.5.0.nupkg");
		await AssertApplyResult(fixture, applyOutput, [archivePath], ["A-v3.5.0"]);
		using (Assert.Multiple())
		{
			await PackageArchiveAssertions.AssertArchive(archivePath, "A", "3.5.0", cancellationToken, ("L", "2.3.4"));
			await Assert.That(fixture.Git.Tags.Select(tag => tag.FriendlyName))
				.IsEquivalentTo(new[] { "F-v1.2.3", "L-v2.3.4", "A-v3.4.5", "U-v4.5.6", "A-v3.5.0" });
		}
		byte[] archiveHash = SHA256.HashData(await File.ReadAllBytesAsync(archivePath, cancellationToken));
		string[] tagsBefore = fixture.Git.Tags.Select(tag => tag.FriendlyName).Order(StringComparer.Ordinal).ToArray();

		(int repeatPlanExitCode, string repeatPlanOutput, string repeatPlanError) = await Run(fixture, cancellationToken,
			"plan", fixture.SolutionPath, "--repository", fixture.DirectoryPath, "--outfile", planPath);
		using (Assert.Multiple())
		{
			await Assert.That(repeatPlanExitCode).IsEqualTo(0).Because(repeatPlanError);
			await Assert.That(repeatPlanOutput).IsEmpty();
		}
		using (JsonDocument repeatPlan = JsonDocument.Parse(await File.ReadAllTextAsync(planPath, cancellationToken)))
		{
			using (Assert.Multiple())
			{
				await Assert.That(repeatPlan.RootElement.GetProperty("Packages").GetArrayLength()).IsEqualTo(0);
				await Assert.That(repeatPlan.RootElement.GetProperty("ReferencedPackages").GetArrayLength()).IsEqualTo(0);
			}
		}
		(int repeatApplyExitCode, string repeatApplyOutput, string repeatApplyError) = await Run(fixture, cancellationToken,
			"apply", "--repository", fixture.DirectoryPath, "--infile", planPath, "--output", fixture.OutputPath);
		await Assert.That(repeatApplyExitCode).IsEqualTo(0).Because(repeatApplyError);
		using JsonDocument repeatResult = JsonDocument.Parse(repeatApplyOutput);
		using (Assert.Multiple())
		{
			await Assert.That(repeatResult.RootElement.GetProperty("PackagePaths").GetArrayLength()).IsEqualTo(0);
			await Assert.That(repeatResult.RootElement.GetProperty("CreatedTags").GetArrayLength()).IsEqualTo(0);
			await Assert.That(Directory.GetFiles(fixture.OutputPath)).IsEquivalentTo(new[] { archivePath });
			await Assert.That(SHA256.HashData(await File.ReadAllBytesAsync(archivePath, cancellationToken))
				.SequenceEqual(archiveHash)).IsTrue();
			await Assert.That(fixture.Git.Tags.Select(tag => tag.FriendlyName)).IsEquivalentTo(tagsBefore);
		}
	}

	private static async Task AssertDependencies(JsonElement[] packages, string packageId, params string[] dependencies)
	{
		JsonElement package = packages.Single(candidate => candidate.GetProperty("PackageId").GetString() == packageId);
		await Assert.That(package.GetProperty("DependencyIds").EnumerateArray().Select(id => id.GetString()!))
			.IsEquivalentTo(dependencies);
	}

	private static async Task AssertApplyResult(RepositoryFixture fixture, string output,
		string[] expectedPaths, string[] expectedTags)
	{
		using JsonDocument document = JsonDocument.Parse(output);
		string[] paths = document.RootElement.GetProperty("PackagePaths").EnumerateArray()
			.Select(path => path.GetString()!).ToArray();
		string[] tags = document.RootElement.GetProperty("CreatedTags").EnumerateArray()
			.Select(tag => tag.GetString()!).ToArray();
		using (Assert.Multiple())
		{
			await Assert.That(paths).IsEquivalentTo(expectedPaths);
			await Assert.That(Directory.GetFiles(fixture.OutputPath)).IsEquivalentTo(expectedPaths);
			await Assert.That(tags).IsEquivalentTo(expectedTags);
			foreach (string tag in expectedTags)
			{
				await Assert.That(fixture.Git.Tags[tag]?.Target.Sha).IsEqualTo(fixture.Git.Head.Tip!.Sha);
			}
		}
	}

	private static async Task<(int ExitCode, string Output, string Error)> Run(
		RepositoryFixture fixture, CancellationToken cancellationToken, params string[] arguments)
	{
		ProcessStartInfo startInfo = new("dotnet")
		{
			WorkingDirectory = fixture.DirectoryPath,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(startInfo)!;
		Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
		try
		{
			await process.WaitForExitAsync(cancellationToken);
			return (process.ExitCode, await output, await error);
		}
		finally
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync(CancellationToken.None);
			}
		}
	}
}
