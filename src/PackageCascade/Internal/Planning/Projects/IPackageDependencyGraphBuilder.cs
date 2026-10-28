namespace PackageCascade.Internal.Planning.Projects;

internal interface IPackageDependencyGraphBuilder
{
	IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> Build(IReadOnlyList<PackableProject> projects);
}
