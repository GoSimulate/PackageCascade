namespace PackageCascade.Internal.Executing.Packaging;

internal interface IPackageBuilder
{
	public Task<IReadOnlyList<string>> Build(PackageBuildInput input, CancellationToken cancellationToken);
}
