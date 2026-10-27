namespace PackageCascade.Internal.Planning;

internal sealed record PackableProject(string ProjectPath, string PackageId, IReadOnlyList<string> ReferencedProjectPaths);
