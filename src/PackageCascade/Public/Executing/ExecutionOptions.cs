namespace PackageCascade.Public.Executing;

/// <summary>Options for applying a prepared release plan locally.</summary>
/// <param name="OutputDirectory">The directory for generated package archives. It is created if needed, and archives with matching filenames are replaced.</param>
/// <param name="IncludeSymbols">Whether to create and verify symbol packages.</param>
public sealed record ExecutionOptions(string OutputDirectory, bool IncludeSymbols = false);
