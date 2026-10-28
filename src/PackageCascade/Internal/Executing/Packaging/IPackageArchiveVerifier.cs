namespace PackageCascade.Internal.Executing.Packaging;

internal interface IPackageArchiveVerifier
{
	public void Verify(IReadOnlyList<string> archivePaths, IReadOnlyList<ExpectedPackage> packages, bool includeSymbols);
}
