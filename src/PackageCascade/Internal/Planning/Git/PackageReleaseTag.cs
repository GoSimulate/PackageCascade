using NuGet.Versioning;

namespace PackageCascade.Internal.Planning.Git;

internal sealed record PackageReleaseTag(string Name, NuGetVersion Version, string CommitSha);
