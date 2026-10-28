using NuGet.Versioning;

namespace PackageCascade.Internal.Planning;

internal sealed record PackageReleaseTag(string Name, NuGetVersion Version, string CommitSha);
