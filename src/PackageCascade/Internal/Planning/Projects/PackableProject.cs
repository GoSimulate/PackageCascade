namespace PackageCascade.Internal.Planning.Projects;

internal sealed record PackableProject(string ProjectPath, string PackageId, IReadOnlyList<string> ReferencedProjectPaths);
