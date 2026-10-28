using System.Xml.Linq;

namespace PackageCascade.Internal.Executing.Packaging;

internal sealed class ProjectVersionMap : IDisposable
{
	private readonly HashSet<string> _paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

	public string ExecutionId { get; } = Guid.NewGuid().ToString("N");

	public void Write(string projectPath, string extensionsPath, string version)
	{
		string path = Path.Combine(extensionsPath, $"{Path.GetFileName(projectPath)}.packagecascade-{ExecutionId}.targets");
		if (_paths.Contains(path))
		{
			return;
		}

		Directory.CreateDirectory(extensionsPath);
		using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write);
		_paths.Add(path);

		// Project-extension imports preserve consumer hooks; the run ID isolates concurrent builds.
		XElement project = new("Project",
			new XElement("PropertyGroup",
				new XAttribute("Condition", $"'$(PackageCascadeExecutionId)' == '{ExecutionId}'"),
				new XElement("Version", version),
				new XElement("PackageVersion", version)));
		new XDocument(project).Save(stream);
	}

	public void Dispose()
	{
		List<Exception> errors = [];
		foreach (string path in _paths)
		{
			try
			{
				File.Delete(path);
			}
			catch (Exception exception)
			{
				errors.Add(exception);
			}
		}

		if (errors.Count > 0)
		{
			throw new AggregateException("Some temporary project version files could not be removed.", errors);
		}
	}
}
