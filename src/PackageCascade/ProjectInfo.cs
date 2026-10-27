namespace PackageCascade;

public static class ProjectInfo
{
	public const string Name = "PackageCascade";

	internal static string AssemblyName => typeof(ProjectInfo).Assembly.GetName().Name!;
}
