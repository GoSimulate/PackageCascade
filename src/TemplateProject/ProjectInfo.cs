namespace TemplateProject;

public static class ProjectInfo
{
	public const string Name = "TemplateProject";

	internal static string AssemblyName => typeof(ProjectInfo).Assembly.GetName().Name!;
}
