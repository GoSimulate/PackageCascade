using System.Text.RegularExpressions;

namespace PackageCascade.Internal.Planning;

internal sealed partial class DirectoryBumpCalculator
{
	public Dictionary<string, SemverBump> Calculate(IEnumerable<GitCommitDetails> commits, IEnumerable<string> directories)
	{
		Dictionary<string, SemverBump> bumps = new(StringComparer.Ordinal);
		foreach (string directory in directories)
		{
			bumps.Add(directory, SemverBump.None);
		}

		foreach (GitCommitDetails commit in commits)
		{
			SemverBump bump = GetBump(commit.Message);
			if (bump == SemverBump.None)
			{
				continue;
			}

			foreach (string directory in bumps.Keys)
			{
				string prefix = directory.TrimEnd('/', '\\').Replace('\\', '/') + "/";
				if (commit.ChangedPaths.Any(path => path.StartsWith(prefix, StringComparison.Ordinal))
					&& bump > bumps[directory])
				{
					bumps[directory] = bump;
				}
			}
		}

		return bumps;
	}

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
