namespace PackageCascade.Internal.Git;

internal interface IRepositoryHeadReader
{
	public string ReadHeadSha(string repositoryPath);
}
