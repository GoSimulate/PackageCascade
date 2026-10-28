using System.Diagnostics;
using System.Text.Json;

namespace PackageCascade.Internal.Executing.Packaging;

internal sealed class PackageBuilder : IPackageBuilder
{
	private readonly IPackageVersionMapWriter _versionMapWriter;

	public PackageBuilder(IPackageVersionMapWriter versionMapWriter)
	{
		_versionMapWriter = versionMapWriter;
	}

	public async Task<IReadOnlyList<string>> Build(PackageBuildInput input, CancellationToken cancellationToken)
	{
		string workingDirectory = Path.GetFullPath(input.WorkingDirectory);
		string outputDirectory = Path.GetFullPath(input.OutputDirectory);
		Directory.CreateDirectory(outputDirectory);

		string executionId = Guid.NewGuid().ToString("N");
		string versionMapProperty = $"-p:PackageCascadeExecutionId={executionId}";
		string configurationProperty = $"-p:Configuration={input.Configuration}";
		PackageVersionMap versionMap = await CreateVersionMap(input.VersionsByProjectPath, executionId, workingDirectory,
			versionMapProperty, configurationProperty, cancellationToken);
		await _versionMapWriter.Write(versionMap, cancellationToken);

		foreach (string projectPath in input.ProjectPaths)
		{
			await RunDotnet(workingDirectory, cancellationToken,
				"restore", Path.GetFullPath(projectPath), versionMapProperty, configurationProperty);
		}

		await EnsureEvaluatedVersions(input.VersionsByProjectPath, workingDirectory,
			versionMapProperty, configurationProperty, cancellationToken);

		foreach (string projectPath in input.ProjectPaths)
		{
			await RunDotnet(workingDirectory, cancellationToken,
				"build", Path.GetFullPath(projectPath), "--no-restore", versionMapProperty, configurationProperty);
			await RunDotnet(workingDirectory, cancellationToken,
				"pack", Path.GetFullPath(projectPath), "--no-build", "--no-restore", "-o", outputDirectory,
				versionMapProperty, configurationProperty,
				$"-p:IncludeSymbols={input.IncludeSymbols.ToString().ToLowerInvariant()}",
				"-p:SymbolPackageFormat=snupkg");
		}

		return Directory.GetFiles(outputDirectory)
			.Where(path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
				|| path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
			.Order(StringComparer.Ordinal)
			.ToArray();
	}

	private static async Task<PackageVersionMap> CreateVersionMap(
		IReadOnlyDictionary<string, string> versionsByProjectPath,
		string executionId,
		string workingDirectory,
		string versionMapProperty,
		string configurationProperty,
		CancellationToken cancellationToken)
	{
		Dictionary<string, string> versionsByTargetPath = new(
			OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		foreach ((string projectPath, string version) in versionsByProjectPath)
		{
			string fullPath = Path.GetFullPath(projectPath);
			string frameworks = (await GetProperty(fullPath, "TargetFrameworks", workingDirectory,
				versionMapProperty, configurationProperty, null, cancellationToken)).Trim();
			foreach (string targetFramework in frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries)
				.Prepend(string.Empty).Distinct(StringComparer.Ordinal))
			{
				string extensionsPath = (await GetProperty(fullPath, "MSBuildProjectExtensionsPath", workingDirectory,
					versionMapProperty, configurationProperty, targetFramework, cancellationToken)).Trim();
				string targetPath = Path.Combine(
					Path.GetFullPath(extensionsPath, Path.GetDirectoryName(fullPath)!),
					$"{Path.GetFileName(fullPath)}.packagecascade-{executionId}.targets");
				versionsByTargetPath.TryAdd(targetPath, version);
			}
		}

		return new PackageVersionMap(executionId, versionsByTargetPath);
	}

	private static async Task EnsureEvaluatedVersions(
		IReadOnlyDictionary<string, string> versionsByProjectPath,
		string workingDirectory,
		string versionMapProperty,
		string configurationProperty,
		CancellationToken cancellationToken)
	{
		foreach ((string projectPath, string expectedVersion) in versionsByProjectPath)
		{
			string fullPath = Path.GetFullPath(projectPath);
			string frameworks = (await GetProperty(fullPath, "TargetFrameworks", workingDirectory,
				versionMapProperty, configurationProperty, null, cancellationToken)).Trim();
			string[] targetFrameworks = frameworks.Length == 0 ? [string.Empty] : frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries);

			foreach (string targetFramework in targetFrameworks.Prepend(string.Empty).Distinct(StringComparer.Ordinal))
			{
				string version = (await GetProperty(fullPath, "Version", workingDirectory,
					versionMapProperty, configurationProperty, targetFramework, cancellationToken)).Trim();
				string packageVersion = (await GetProperty(fullPath, "PackageVersion", workingDirectory,
					versionMapProperty, configurationProperty, targetFramework, cancellationToken)).Trim();
				if (version != expectedVersion || packageVersion != expectedVersion)
				{
					throw new InvalidOperationException($"Project '{fullPath}' evaluates Version='{version}' and PackageVersion='{packageVersion}' for '{targetFramework}', expected '{expectedVersion}'.");
				}
			}
		}
	}

	private static async Task<string> GetProperty(
		string projectPath, string propertyName, string workingDirectory,
		string versionMapProperty, string configurationProperty, string? targetFramework,
		CancellationToken cancellationToken)
	{
		List<string> arguments = ["msbuild", projectPath, $"-getProperty:{propertyName},MSBuildProjectFullPath", versionMapProperty, configurationProperty];
		if (!string.IsNullOrEmpty(targetFramework))
		{
			arguments.Add($"-p:TargetFramework={targetFramework}");
		}

		string output = await RunDotnet(workingDirectory, cancellationToken, arguments.ToArray());
		// SDK first-use notices can precede MSBuild's structured property output.
		int jsonStart = output.IndexOf('{');
		if (jsonStart < 0)
		{
			throw new InvalidOperationException($"MSBuild returned no property data for '{projectPath}'.\n{output}");
		}

		using JsonDocument document = JsonDocument.Parse(output.AsMemory(jsonStart));
		return document.RootElement.GetProperty("Properties").GetProperty(propertyName).GetString() ?? string.Empty;
	}

	private static async Task<string> RunDotnet(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
	{
		ProcessStartInfo startInfo = new("dotnet")
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		// MSBuildLocator sets these for the host; child dotnet must select the consumer's SDK.
		startInfo.Environment.Remove("MSBUILD_EXE_PATH");
		startInfo.Environment.Remove("MSBuildExtensionsPath");
		startInfo.Environment.Remove("MSBuildSDKsPath");
		startInfo.Environment["DOTNET_NOLOGO"] = "true";

		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start dotnet.");
		Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
		try
		{
			await process.WaitForExitAsync(cancellationToken);
			await Task.WhenAll(output, error);
		}
		catch (OperationCanceledException)
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync(CancellationToken.None);
			}

			throw;
		}

		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException($"dotnet {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.\n{await output}\n{await error}");
		}

		return await output;
	}
}
