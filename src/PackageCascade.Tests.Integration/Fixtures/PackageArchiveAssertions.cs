using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using NuGet.Versioning;

namespace PackageCascade.Tests.Integration.Fixtures;

internal static class PackageArchiveAssertions
{
	public static async Task AssertArchive(string path, string packageId, string version,
		CancellationToken cancellationToken, params (string Id, string Version)[] expectedDependencies)
	{
		await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
			bufferSize: 4096, options: FileOptions.Asynchronous);
		await using ZipArchive archive = await ZipArchive.CreateAsync(file, ZipArchiveMode.Read,
			leaveOpen: true, entryNameEncoding: null, cancellationToken: cancellationToken);
		ZipArchiveEntry manifest = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
		await using Stream stream = await manifest.OpenAsync(cancellationToken);
		using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true });
		XDocument document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
		XElement metadata = document.Root!.Elements().Single(element => element.Name.LocalName == "metadata");
		XElement[] dependencies = metadata.Descendants().Where(element => element.Name.LocalName == "dependency").ToArray();

		using (Assert.Multiple())
		{
			await Assert.That(metadata.Elements().Single(element => element.Name.LocalName == "id").Value).IsEqualTo(packageId);
			await Assert.That(metadata.Elements().Single(element => element.Name.LocalName == "version").Value).IsEqualTo(version);
			await Assert.That(dependencies.Select(element => element.Attribute("id")!.Value))
				.IsEquivalentTo(expectedDependencies.Select(dependency => dependency.Id));
			foreach ((string id, string dependencyVersion) in expectedDependencies)
			{
				XElement dependency = dependencies.Single(element => (string?)element.Attribute("id") == id);
				VersionRange range = VersionRange.Parse(dependency.Attribute("version")!.Value);
				await Assert.That(range.MinVersion).IsEqualTo(NuGetVersion.Parse(dependencyVersion));
				await Assert.That(range.IsMinInclusive).IsTrue();
			}
		}
	}
}
