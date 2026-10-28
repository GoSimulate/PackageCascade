namespace PackageCascade.Internal.Planning.Projects;

internal interface ISolutionPackageDiscoverer
{
	IReadOnlyList<PackableProject> Discover(string solutionPath);
}
