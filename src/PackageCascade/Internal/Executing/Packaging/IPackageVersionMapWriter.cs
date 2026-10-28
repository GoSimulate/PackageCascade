namespace PackageCascade.Internal.Executing.Packaging;

internal interface IPackageVersionMapWriter
{
	public Task Write(PackageVersionMap versionMap, CancellationToken cancellationToken = default);
}
