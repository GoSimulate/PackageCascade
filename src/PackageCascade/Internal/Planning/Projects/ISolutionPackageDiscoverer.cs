namespace PackageCascade.Internal.Planning.Projects;

internal interface ISolutionPackageDiscoverer
{
	public IReadOnlyList<PackableProject> Discover(string solutionPath, string configuration = "Release");
}
