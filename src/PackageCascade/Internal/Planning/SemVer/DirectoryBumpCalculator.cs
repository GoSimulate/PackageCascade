using PackageCascade.Internal.Planning.Git;
using PackageCascade.Public.Definition;
using System.Text.RegularExpressions;

namespace PackageCascade.Internal.Planning.SemVer;

internal sealed partial class DirectoryBumpCalculator : IDirectoryBumpCalculator
{
	public Dictionary<string, SemverBump> Calculate(IEnumerable<GitCommitDetails> commits, IEnumerable<string> directories)
	{
		Dictionary<string, SemverBump> bumps = new(StringComparer.Ordinal);
		Dictionary<string, string> directoryPrefixes = new(StringComparer.Ordinal);

		foreach (string directory in directories)
		{
			bumps.Add(directory, SemverBump.None);
			directoryPrefixes.Add(directory, directory is "." or "" ? "" : directory.TrimEnd('/', '\\').Replace('\\', '/') + "/");
		}

		foreach (GitCommitDetails commit in commits)
		{
			SemverBump bump = GetBump(commit.Message);
			bool changesRootSharedFile = commit.ChangedPaths.Any(IsRootSharedFile);
			if (changesRootSharedFile && bump < SemverBump.Patch)
			{
				bump = SemverBump.Patch;
			}

			if (bump == SemverBump.None)
			{
				continue;
			}

			ApplyBumpToAffectedDirectories(commit, bump, changesRootSharedFile, directoryPrefixes, bumps);
		}

		return bumps;
	}

	private static void ApplyBumpToAffectedDirectories(GitCommitDetails commit, SemverBump bump, bool changesRootSharedFile,
		IReadOnlyDictionary<string, string> directoryPrefixes, Dictionary<string, SemverBump> bumps)
	{
		foreach ((string directory, string prefix) in directoryPrefixes)
		{
			if ((changesRootSharedFile || commit.ChangedPaths.Any(path => path.StartsWith(prefix, StringComparison.Ordinal)))
				&& bump > bumps[directory])
			{
				bumps[directory] = bump;
			}
		}
	}

	private static bool IsRootSharedFile(string path) => path is
		"Directory.Build.props" or
		"Directory.Build.targets" or
		"Directory.Packages.props" or
		"global.json" or
		"NuGet.Config";

	private static SemverBump GetBump(string message)
	{
		Match header = HeaderPattern().Match(message);
		if (!header.Success)
		{
			return SemverBump.None;
		}

		if (header.Groups["breaking"].Success || BreakingFooterPattern().IsMatch(message))
		{
			return SemverBump.Major;
		}

		return header.Groups["type"].Value switch
		{
			"feat" => SemverBump.Minor,
			"fix" or "perf" => SemverBump.Patch,
			_ => SemverBump.None,
		};
	}

	[GeneratedRegex(@"\A(?<type>[a-z]+)(?:\([^)]+\))?(?<breaking>!)?: ")]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"(?m)^BREAKING(?: CHANGE|-CHANGE):")]
	private static partial Regex BreakingFooterPattern();
}
