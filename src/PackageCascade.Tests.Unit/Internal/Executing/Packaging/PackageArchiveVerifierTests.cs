using System.IO.Compression;
using PackageCascade.Internal.Executing.Packaging;

namespace PackageCascade.Tests.Unit.Internal.Executing.Packaging;

public sealed class PackageArchiveVerifierTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("packagecascade-archive-verifier-").FullName;
	private readonly PackageArchiveVerifier _verifier = new();

	[Test]
	public void AcceptsNamespacedGroupedDependenciesAndSymbols()
	{
		string grouped = Archive("A.nupkg", Manifest("A", "1.2.3", """
			<dependencies><group targetFramework="net11.0"><dependency id="B" version="[2.0.0,)" /></group><group targetFramework="net10.0"><dependency id="Other" version="1.0.0" /></group></dependencies>
			""", namespaced: true));
		string ungrouped = Archive("B.nupkg", Manifest("B", "2.0.0", """
			<dependencies><dependency id="Other" version="1.0.0" /></dependencies>
			"""));
		string symbolsA = Archive("A.snupkg", Manifest("A", "1.2.3"));
		string symbolsB = Archive("B.snupkg", Manifest("B", "2.0.0"));
		ExpectedPackage a = Expected("A", "1.2.3", new Dictionary<string, string> { ["B"] = "2.0.0" });

		_verifier.Verify([grouped, ungrouped, symbolsA, symbolsB], [a, Expected("B", "2.0.0")], includeSymbols: true);
	}

	[Test]
	[Arguments("2.0.0")]
	[Arguments("[2.0.0]")]
	[Arguments("[2.0.0,3.0.0)")]
	public void AcceptsUngroupedDependencyWithPlannedMinimum(string range)
	{
		string archive = Archive("A.nupkg", Manifest("A", "1.2.3",
			$"<dependencies><dependency id=\"B\" version=\"{range}\" /></dependencies>"));
		_verifier.Verify([archive], [Expected("A", "1.2.3", new Dictionary<string, string> { ["B"] = "2.0.0" })], false);
	}

	[Test]
	public void RejectsMissingPackageArchive()
	{
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([], [Expected("A", "1.0.0")], false));
	}

	[Test]
	public void RejectsDuplicatePackageArchive()
	{
		string a = Archive("A.nupkg", Manifest("A", "1.0.0"));
		string duplicate = Archive("copy.nupkg", Manifest("A", "1.0.0"));
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([a, duplicate], [Expected("A", "1.0.0")], false));
	}

	[Test]
	public void RejectsUnexpectedPackageArchive()
	{
		string a = Archive("A.nupkg", Manifest("A", "1.0.0"));
		string unexpected = Archive("other.nupkg", Manifest("Other", "1.0.0"));
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([a, unexpected], [Expected("A", "1.0.0")], false));
	}

	[Test]
	public void RejectsMissingSymbolArchive()
	{
		string a = Archive("A.nupkg", Manifest("A", "1.0.0"));
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([a], [Expected("A", "1.0.0")], true));
	}

	[Test]
	public void RejectsDuplicateSymbolArchive()
	{
		string a = Archive("A.nupkg", Manifest("A", "1.0.0"));
		string symbols = Archive("A.snupkg", Manifest("A", "1.0.0"));
		string duplicateSymbols = Archive("copy.snupkg", Manifest("A", "1.0.0"));
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([a, symbols, duplicateSymbols], [Expected("A", "1.0.0")], true));
	}

	[Test]
	[Arguments(false, "B", "1.0.0")]
	[Arguments(false, "A", "2.0.0")]
	[Arguments(true, "B", "1.0.0")]
	[Arguments(true, "A", "2.0.0")]
	public void RejectsWrongIdentityOrVersion(bool isSymbol, string archiveId, string archiveVersion)
	{
		string archive = Archive(isSymbol ? "A.snupkg" : "A.nupkg", Manifest(archiveId, archiveVersion));
		string[] archives = isSymbol ? [Archive("A.nupkg", Manifest("A", "1.0.0")), archive] : [archive];
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify(archives, [Expected("A", "1.0.0")], false));
	}

	[Test]
	public void RejectsCorruptArchive()
	{
		string corrupt = Path.Combine(_directory, "corrupt.nupkg");
		File.WriteAllText(corrupt, "not a zip");
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([corrupt], [Expected("A", "1.0.0")], false));
	}

	[Test]
	public void RejectsArchiveWithoutManifest()
	{
		string noManifest = Archive("empty.nupkg");
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([noManifest], [Expected("A", "1.0.0")], false));
	}

	[Test]
	public void RejectsNestedManifest()
	{
		string nested = Archive("nested.nupkg", Manifest("A", "1.0.0"), "nested/A.nuspec");
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([nested], [Expected("A", "1.0.0")], false));
	}

	[Test]
	public void RejectsMultipleRootManifests()
	{
		string twoManifests = Archive("two.nupkg", Manifest("A", "1.0.0"), "A.nuspec", "B.nuspec");
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([twoManifests], [Expected("A", "1.0.0")], false));
	}

	[Test]
	public void RejectsMalformedManifest()
	{
		string malformed = Archive("malformed.nupkg", "<package><metadata>");
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([malformed], [Expected("A", "1.0.0")], false));
	}

	[Test]
	public void RejectsManifestWithDtd()
	{
		string dtd = Archive("dtd.nupkg", "<!DOCTYPE package [<!ENTITY x 'A'>]><package><metadata><id>&x;</id><version>1.0.0</version></metadata></package>");
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([dtd], [Expected("A", "1.0.0")], false));
	}

	[Test]
	public void RejectsMissingDependency()
	{
		ExpectedPackage package = Expected("A", "1.0.0", new Dictionary<string, string> { ["B"] = "2.0.0" });
		string missing = Archive("missing-dependency.nupkg", Manifest("A", "1.0.0"));
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([missing], [package], false));
	}

	[Test]
	public void RejectsStaleDependencyOccurrenceInAnotherGroup()
	{
		ExpectedPackage package = Expected("A", "1.0.0", new Dictionary<string, string> { ["B"] = "2.0.0" });
		string stale = Archive("stale-dependency.nupkg", Manifest("A", "1.0.0", """
			<dependencies><group targetFramework="net10.0"><dependency id="B" version="[2.0.0,)" /></group><group targetFramework="net11.0"><dependency id="B" version="[1.0.0,)" /></group></dependencies>
			"""));
		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([stale], [package], false));
	}

	[Test]
	[Arguments("(2.0.0,)")]
	[Arguments("[1.0.0,)")]
	[Arguments("[3.0.0,)")]
	[Arguments("not-a-range")]
	[Arguments("2.*")]
	public void RejectsInvalidDependencyRange(string range)
	{
		string archive = Archive("A.nupkg", Manifest("A", "1.0.0", $"<dependencies><dependency id=\"B\" version=\"{range}\" /></dependencies>"));
		ExpectedPackage package = Expected("A", "1.0.0", new Dictionary<string, string> { ["B"] = "2.0.0" });

		Assert.Throws(typeof(InvalidOperationException), () => _verifier.Verify([archive], [package], false));
	}

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	private string Archive(string fileName, string? manifest = null, params string[] entryNames)
	{
		string path = Path.Combine(_directory, fileName);
		using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
		foreach (string name in entryNames.Length == 0 && manifest is not null ? ["package.nuspec"] : entryNames)
		{
			using StreamWriter writer = new(archive.CreateEntry(name).Open());
			writer.Write(manifest);
		}

		return path;
	}

	private static string Manifest(string id, string version, string dependencies = "", bool namespaced = false)
	{
		string namespaceAttribute = namespaced ? " xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"" : "";
		return $"<package{namespaceAttribute}><metadata><id>{id}</id><version>{version}</version>{dependencies}</metadata></package>";
	}

	private static ExpectedPackage Expected(string id, string version, IReadOnlyDictionary<string, string>? dependencies = null)
	{
		return new(id, version, dependencies ?? new Dictionary<string, string>());
	}
}
