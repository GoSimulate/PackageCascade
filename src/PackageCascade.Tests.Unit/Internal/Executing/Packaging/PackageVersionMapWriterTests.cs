using System.Xml.Linq;
using PackageCascade.Internal.Executing.Packaging;

namespace PackageCascade.Tests.Unit.Internal.Executing.Packaging;

public sealed class PackageVersionMapWriterTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-map-writer-").FullName;

	[Test]
	public async Task WritesConditionalVersions()
	{
		string path = Path.Combine(_directory, "obj", "A.csproj.packagecascade-run123.targets");
		PackageVersionMap map = new("run123", new Dictionary<string, string> { [path] = "2.3.4" });

		await new PackageVersionMapWriter().Write(map);

		XDocument document = XDocument.Load(path);
		XElement group = document.Root!.Element("PropertyGroup")!;
		using (Assert.Multiple())
		{
			await Assert.That((string?)group.Attribute("Condition")).IsEqualTo("'$(PackageCascadeExecutionId)' == 'run123'");
			await Assert.That((string?)group.Element("Version")).IsEqualTo("2.3.4");
			await Assert.That((string?)group.Element("PackageVersion")).IsEqualTo("2.3.4");
		}
	}

	[Test]
	public async Task ExistingTargetIsNotOverwritten()
	{
		string createdPath = Path.Combine(_directory, "A.csproj.packagecascade-run123.targets");
		string existingPath = Path.Combine(_directory, "B.csproj.packagecascade-run123.targets");
		File.WriteAllText(existingPath, "consumer content");
		PackageVersionMap map = new("run123", new Dictionary<string, string>
		{
			[createdPath] = "2.3.4",
			[existingPath] = "3.4.5",
		});

		await Assert.ThrowsAsync<IOException>(() => new PackageVersionMapWriter().Write(map));

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(createdPath)).IsTrue();
			await Assert.That(File.ReadAllText(existingPath)).IsEqualTo("consumer content");
		}
	}

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}
}
