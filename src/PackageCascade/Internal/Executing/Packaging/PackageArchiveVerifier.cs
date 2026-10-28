using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using NuGet.Versioning;

namespace PackageCascade.Internal.Executing.Packaging;

internal sealed class PackageArchiveVerifier : IPackageArchiveVerifier
{
	public async Task Verify(IReadOnlyList<string> archivePaths, IReadOnlyList<ExpectedPackage> packages,
		bool includeSymbols, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Dictionary<string, ExpectedPackage> expected = IndexExpectedPackages(packages);
		HashSet<string> packageIds = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> symbolIds = new(StringComparer.OrdinalIgnoreCase);
		foreach (string path in archivePaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await VerifyArchive(path, expected, packageIds, symbolIds, cancellationToken);
		}

		cancellationToken.ThrowIfCancellationRequested();
		VerifyCompleteness(packages, includeSymbols, packageIds, symbolIds);
	}

	private static Dictionary<string, ExpectedPackage> IndexExpectedPackages(IReadOnlyList<ExpectedPackage> packages)
	{
		Dictionary<string, ExpectedPackage> expected = new(StringComparer.OrdinalIgnoreCase);
		foreach (ExpectedPackage package in packages)
		{
			if (!expected.TryAdd(package.PackageId, package))
			{
				throw new InvalidOperationException($"Package '{package.PackageId}' is expected more than once.");
			}
		}

		return expected;
	}

	private static async Task VerifyArchive(string path, Dictionary<string, ExpectedPackage> expected,
		HashSet<string> packageIds, HashSet<string> symbolIds, CancellationToken cancellationToken)
	{
		bool isSymbol = path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase);
		if (!isSymbol && !path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Unexpected archive type: '{path}'.");
		}

		XElement metadata = await ReadMetadata(path, cancellationToken);
		ExpectedPackage package = VerifyIdentity(metadata, expected, path);
		HashSet<string> seen = isSymbol ? symbolIds : packageIds;
		if (!seen.Add(package.PackageId))
		{
			throw new InvalidOperationException($"Duplicate {(isSymbol ? "symbol" : "package")} archive for '{package.PackageId}'.");
		}

		if (!isSymbol)
		{
			VerifyDependencies(metadata, package, path);
		}
	}

	private static ExpectedPackage VerifyIdentity(XElement metadata, Dictionary<string, ExpectedPackage> expected, string path)
	{
		string id = RequiredElement(metadata, "id", path).Value;
		string versionText = RequiredElement(metadata, "version", path).Value;
		if (!expected.TryGetValue(id, out ExpectedPackage? package)
			|| !NuGetVersion.TryParse(versionText, out NuGetVersion? version)
			|| !NuGetVersion.TryParse(package.Version, out NuGetVersion? expectedVersion)
			|| version != expectedVersion)
		{
			throw new InvalidOperationException($"Unexpected package identity '{id}' version '{versionText}' in '{path}'.");
		}

		return package;
	}

	private static void VerifyCompleteness(IReadOnlyList<ExpectedPackage> packages, bool includeSymbols,
		HashSet<string> packageIds, HashSet<string> symbolIds)
	{
		foreach (ExpectedPackage package in packages)
		{
			if (!packageIds.Contains(package.PackageId))
			{
				throw new InvalidOperationException($"Missing package archive for '{package.PackageId}' version '{package.Version}'.");
			}

			if (includeSymbols && !symbolIds.Contains(package.PackageId))
			{
				throw new InvalidOperationException($"Missing symbol archive for '{package.PackageId}' version '{package.Version}'.");
			}
		}
	}

	private static async Task<XElement> ReadMetadata(string path, CancellationToken cancellationToken)
	{
		try
		{
			await using FileStream file = new(path, new FileStreamOptions
			{
				Mode = FileMode.Open,
				Access = FileAccess.Read,
				Share = FileShare.Read,
				Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
			});
			await using ZipArchive archive = await ZipArchive.CreateAsync(file, ZipArchiveMode.Read,
				leaveOpen: true, entryNameEncoding: null, cancellationToken);
			ZipArchiveEntry manifest = FindManifest(archive, path);
			return await LoadMetadata(manifest, path, cancellationToken);
		}
		catch (Exception exception) when (exception is InvalidDataException or XmlException)
		{
			throw new InvalidOperationException($"Archive '{path}' has an invalid nuspec or package format.", exception);
		}
	}

	private static ZipArchiveEntry FindManifest(ZipArchive archive, string path)
	{
		ZipArchiveEntry[] manifests = archive.Entries
			.Where(entry => !entry.FullName.Contains('/') && !entry.FullName.Contains('\\')
				&& entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
			.ToArray();
		if (manifests.Length != 1)
		{
			throw new InvalidOperationException($"Archive '{path}' must contain exactly one root nuspec.");
		}

		return manifests[0];
	}

	private static async Task<XElement> LoadMetadata(ZipArchiveEntry manifest, string path, CancellationToken cancellationToken)
	{
		await using Stream stream = await manifest.OpenAsync(cancellationToken);
		using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
		{
			Async = true,
			DtdProcessing = DtdProcessing.Prohibit,
			XmlResolver = null,
		});
		XElement root = (await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken)).Root
			?? throw new InvalidOperationException($"Archive '{path}' has an empty nuspec.");
		if (root.Name.LocalName != "package")
		{
			throw new InvalidOperationException($"Archive '{path}' has an invalid nuspec root.");
		}

		return root.Element(root.Name.Namespace + "metadata")
			?? throw new InvalidOperationException($"Archive '{path}' has no nuspec metadata.");
	}

	private static XElement RequiredElement(XElement parent, string name, string path)
	{
		return parent.Element(parent.Name.Namespace + name)
			?? throw new InvalidOperationException($"Archive '{path}' is missing nuspec {name}.");
	}

	private static void VerifyDependencies(XElement metadata, ExpectedPackage package, string path)
	{
		Dictionary<string, NuGetVersion> expectedDependencies = ParseExpectedDependencies(package);
		HashSet<string> found = new(StringComparer.OrdinalIgnoreCase);
		foreach (XElement dependency in GetDependencyEntries(metadata))
		{
			string? id = (string?)dependency.Attribute("id");
			if (id is null || !expectedDependencies.TryGetValue(id, out NuGetVersion? expectedVersion))
			{
				continue;
			}

			VerifyDependencyRange(dependency, package.PackageId, id, expectedVersion, path);
			found.Add(id);
		}

		foreach (string id in expectedDependencies.Keys)
		{
			if (!found.Contains(id))
			{
				throw new InvalidOperationException($"Package '{package.PackageId}' is missing dependency '{id}' in '{path}'.");
			}
		}
	}

	private static Dictionary<string, NuGetVersion> ParseExpectedDependencies(ExpectedPackage package)
	{
		Dictionary<string, NuGetVersion> expectedDependencies = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string id, string version) in package.DirectDependencyVersions)
		{
			if (!NuGetVersion.TryParse(version, out NuGetVersion? parsed))
			{
				throw new InvalidOperationException($"Invalid expected dependency version '{version}' for '{id}'.");
			}

			expectedDependencies.Add(id, parsed);
		}

		return expectedDependencies;
	}

	private static IEnumerable<XElement> GetDependencyEntries(XElement metadata)
	{
		XElement? dependencies = metadata.Element(metadata.Name.Namespace + "dependencies");
		if (dependencies is null)
		{
			return [];
		}

		return dependencies.Elements(dependencies.Name.Namespace + "dependency")
			.Concat(dependencies.Elements(dependencies.Name.Namespace + "group")
				.SelectMany(group => group.Elements(group.Name.Namespace + "dependency")));
	}

	private static void VerifyDependencyRange(XElement dependency, string packageId, string id,
		NuGetVersion expectedVersion, string path)
	{
		string? version = (string?)dependency.Attribute("version");
		if (version is null || !VersionRange.TryParse(version, out VersionRange? range)
			|| range.IsFloating || !range.IsMinInclusive || range.MinVersion != expectedVersion)
		{
			throw new InvalidOperationException($"Package '{packageId}' has an invalid dependency range '{version}' for '{id}' in '{path}'; expected an inclusive minimum of '{expectedVersion}'.");
		}
	}
}
