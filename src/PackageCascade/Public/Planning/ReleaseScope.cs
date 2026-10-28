namespace PackageCascade.Public.Planning;

/// <summary>Where a conventional commit's relevant changes were found.</summary>
public enum ReleaseScope
{
	/// <summary>Changes within the package's project directory.</summary>
	Project,

	/// <summary>Changes to build inputs shared by multiple packages.</summary>
	Shared,
}
