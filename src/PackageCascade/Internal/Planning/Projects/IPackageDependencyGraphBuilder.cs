namespace PackageCascade.Internal.Planning.Projects;

internal interface IPackageDependencyGraphBuilder
{
	public IReadOnlyDictionary<PackableProject, IReadOnlyList<PackableProject>> Build(IReadOnlyList<PackableProject> projects);
}
