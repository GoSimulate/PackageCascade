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

	public async Task<IReadOnlyList<string>> CopyVerifiedArchives(IReadOnlyList<string> archivePaths, CancellationToken cancellationToken)
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
			cancellationToken.ThrowIfCancellationRequested();
			string source = Path.GetFullPath(archivePath);
			if (!string.Equals(Path.GetDirectoryName(source), _stagingPath, pathComparison))
			{
				throw new InvalidOperationException($"Verified archive '{archivePath}' is outside package staging.");
			}

			string destination = Path.Combine(_path, Path.GetFileName(source));
			await using FileStream output = new(destination, new FileStreamOptions
			{
				Mode = FileMode.Create,
				Access = FileAccess.Write,
				Options = FileOptions.Asynchronous,
			});
			copied.Add(destination);
			await using FileStream input = new(source, new FileStreamOptions
			{
				Mode = FileMode.Open,
				Access = FileAccess.Read,
				Share = FileShare.Read,
				Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
			});
			await input.CopyToAsync(output, cancellationToken);
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
