using PackageCascade.Internal.Planning.Git;
using PackageCascade.Public.Planning.Definition;
namespace PackageCascade.Internal.Planning.SemVer;

internal interface IDirectoryBumpCalculator
{
	public Dictionary<string, SemverBump> Calculate(IEnumerable<GitCommitDetails> commits, IEnumerable<string> directories);
}
