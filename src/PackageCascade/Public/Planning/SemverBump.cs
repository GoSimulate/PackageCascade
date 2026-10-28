namespace PackageCascade.Public.Planning;

/// <summary>The strength of a required semantic version increase.</summary>
public enum SemverBump
{
	/// <summary>No release is required.</summary>
	None,

	/// <summary>Increase the patch component.</summary>
	Patch,

	/// <summary>Increase the minor component and reset the patch component.</summary>
	Minor,

	/// <summary>Increase the major component and reset the minor and patch components.</summary>
	Major,
}
