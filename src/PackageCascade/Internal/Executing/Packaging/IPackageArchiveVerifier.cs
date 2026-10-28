namespace PackageCascade.Internal.Executing.Packaging;

internal interface IPackageArchiveVerifier
{
	public Task Verify(IReadOnlyList<string> archivePaths, IReadOnlyList<ExpectedPackage> packages,
		bool includeSymbols, CancellationToken cancellationToken = default);
}
