namespace PackageCascade.Public.Executing;

/// <summary>Options for applying a prepared release plan locally.</summary>
/// <param name="OutputDirectory">The directory for generated package archives; it may be created by the executor, and existing files are preserved.</param>
/// <param name="IncludeSymbols">Whether to create and verify symbol packages.</param>
public sealed record ExecutionOptions(string OutputDirectory, bool IncludeSymbols = false);
