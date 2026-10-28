namespace PackageCascade.Internal.Executing;

internal sealed class OutputDirectoryScope
{
	private readonly string _path;
	private string? _stagingPath;

	public OutputDirectoryScope(string path)
	{
		_path = Path.GetFullPath(path);
		if (File.Exists(_path))
		{
			throw new InvalidOperationException($"Output path '{_path}' is a file.");
		}
	}

	public string OutputPath => _path;

	public string CreateStagingDirectory()
	{
		return _stagingPath ??= Directory.CreateTempSubdirectory("packagecascade-apply-").FullName;
	}

	public IReadOnlyList<string> CopyVerifiedArchives(IReadOnlyList<string> archivePaths)
	{
		if (_stagingPath is null)
		{
			throw new InvalidOperationException("Package staging has not started.");
		}

		Directory.CreateDirectory(_path);
		List<string> copied = [];
		StringComparison pathComparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		foreach (string archivePath in archivePaths)
		{
			string source = Path.GetFullPath(archivePath);
			if (!string.Equals(Path.GetDirectoryName(source), _stagingPath, pathComparison))
			{
				throw new InvalidOperationException($"Verified archive '{archivePath}' is outside package staging.");
			}

			string destination = Path.Combine(_path, Path.GetFileName(source));
			using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write);
			copied.Add(destination);
			using FileStream input = File.OpenRead(source);
			input.CopyTo(output);
		}

		return copied;
	}

	public void FinishStaging()
	{
		if (_stagingPath is not null)
		{
			Directory.Delete(_stagingPath, recursive: true);
			_stagingPath = null;
		}
	}
}
