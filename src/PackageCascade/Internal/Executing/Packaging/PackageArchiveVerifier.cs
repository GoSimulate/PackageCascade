using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using NuGet.Versioning;

namespace PackageCascade.Internal.Executing.Packaging;

internal sealed class PackageArchiveVerifier : IPackageArchiveVerifier
{
	public void Verify(IReadOnlyList<string> archivePaths, IReadOnlyList<ExpectedPackage> packages, bool includeSymbols)
	{
		Dictionary<string, ExpectedPackage> expected = new(StringComparer.OrdinalIgnoreCase);
		foreach (ExpectedPackage package in packages)
		{
			if (!expected.TryAdd(package.PackageId, package))
			{
				throw new InvalidOperationException($"Package '{package.PackageId}' is expected more than once.");
			}
		}

		HashSet<string> packageIds = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> symbolIds = new(StringComparer.OrdinalIgnoreCase);
		foreach (string path in archivePaths)
		{
			bool isSymbol = path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase);
			if (!isSymbol && !path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"Unexpected archive type: '{path}'.");
			}

			XElement metadata = ReadMetadata(path);
			string id = RequiredElement(metadata, "id", path).Value;
			string versionText = RequiredElement(metadata, "version", path).Value;
			if (!expected.TryGetValue(id, out ExpectedPackage? package)
				|| !NuGetVersion.TryParse(versionText, out NuGetVersion? version)
				|| !NuGetVersion.TryParse(package.Version, out NuGetVersion? expectedVersion)
				|| version != expectedVersion)
			{
				throw new InvalidOperationException($"Unexpected package identity '{id}' version '{versionText}' in '{path}'.");
			}

			HashSet<string> seen = isSymbol ? symbolIds : packageIds;
			if (!seen.Add(id))
			{
				throw new InvalidOperationException($"Duplicate {(isSymbol ? "symbol" : "package")} archive for '{id}'.");
			}

			if (!isSymbol)
			{
				VerifyDependencies(metadata, package, path);
			}
		}

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

	private static XElement ReadMetadata(string path)
	{
		try
		{
			using ZipArchive archive = ZipFile.OpenRead(path);
			ZipArchiveEntry[] manifests = archive.Entries
				.Where(entry => !entry.FullName.Contains('/') && !entry.FullName.Contains('\\')
					&& entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
				.ToArray();
			if (manifests.Length != 1)
			{
				throw new InvalidOperationException($"Archive '{path}' must contain exactly one root nuspec.");
			}

			using Stream stream = manifests[0].Open();
			using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
			{
				DtdProcessing = DtdProcessing.Prohibit,
				XmlResolver = null,
			});
			XElement root = XDocument.Load(reader).Root
				?? throw new InvalidOperationException($"Archive '{path}' has an empty nuspec.");
			if (root.Name.LocalName != "package")
			{
				throw new InvalidOperationException($"Archive '{path}' has an invalid nuspec root.");
			}

			return root.Element(root.Name.Namespace + "metadata")
				?? throw new InvalidOperationException($"Archive '{path}' has no nuspec metadata.");
		}
		catch (Exception exception) when (exception is InvalidDataException or XmlException)
		{
			throw new InvalidOperationException($"Archive '{path}' has an invalid nuspec or package format.", exception);
		}
	}

	private static XElement RequiredElement(XElement parent, string name, string path)
	{
		return parent.Element(parent.Name.Namespace + name)
			?? throw new InvalidOperationException($"Archive '{path}' is missing nuspec {name}.");
	}

	private static void VerifyDependencies(XElement metadata, ExpectedPackage package, string path)
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

		HashSet<string> found = new(StringComparer.OrdinalIgnoreCase);
		XElement? dependencies = metadata.Element(metadata.Name.Namespace + "dependencies");
		if (dependencies is not null)
		{
			IEnumerable<XElement> entries = dependencies.Elements(dependencies.Name.Namespace + "dependency")
				.Concat(dependencies.Elements(dependencies.Name.Namespace + "group")
					.SelectMany(group => group.Elements(group.Name.Namespace + "dependency")));
			foreach (XElement dependency in entries)
			{
				string? id = (string?)dependency.Attribute("id");
				if (id is null || !expectedDependencies.TryGetValue(id, out NuGetVersion? expectedVersion))
				{
					continue;
				}

				string? version = (string?)dependency.Attribute("version");
				if (version is null || !VersionRange.TryParse(version, out VersionRange? range)
					|| range.IsFloating || !range.IsMinInclusive || range.MinVersion != expectedVersion)
				{
					throw new InvalidOperationException($"Package '{package.PackageId}' has an invalid dependency range '{version}' for '{id}' in '{path}'; expected an inclusive minimum of '{expectedVersion}'.");
				}

				found.Add(id);
			}
		}

		foreach (string id in expectedDependencies.Keys)
		{
			if (!found.Contains(id))
			{
				throw new InvalidOperationException($"Package '{package.PackageId}' is missing dependency '{id}' in '{path}'.");
			}
		}
	}
}
