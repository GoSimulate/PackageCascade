using System.Xml.Linq;

namespace PackageCascade.Internal.Executing.Packaging;

internal sealed class PackageVersionMapWriter : IPackageVersionMapWriter
{
	public async Task Write(PackageVersionMap versionMap, CancellationToken cancellationToken = default)
	{
		foreach ((string path, string version) in versionMap.VersionsByTargetPath)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			await using FileStream stream = new(path, new FileStreamOptions
			{
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Options = FileOptions.Asynchronous,
			});

			// Project-extension imports preserve consumer hooks; the run ID isolates concurrent builds.
			XElement project = new("Project",
				new XElement("PropertyGroup",
					new XAttribute("Condition", $"'$(PackageCascadeExecutionId)' == '{versionMap.ExecutionId}'"),
					new XElement("Version", version),
					new XElement("PackageVersion", version)));
			await new XDocument(project).SaveAsync(stream, SaveOptions.None, cancellationToken);
		}
	}
}
