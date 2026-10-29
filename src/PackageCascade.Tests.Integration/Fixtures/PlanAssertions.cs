using PackageCascade.Public.Definition;

namespace PackageCascade.Tests.Integration.Fixtures;

internal static class PlanAssertions
{
	public static async Task HasReleases(ReleasePlan plan,
		params (string Id, string Version, string[] Dependencies)[] expected)
	{
		using (Assert.Multiple())
		{
			await Assert.That(plan.Packages.Select(package => (package.PackageId, package.Version)))
				.IsEquivalentTo(expected.Select(package => (package.Id, package.Version)));
			foreach ((string id, string _, string[] dependencies) in expected)
			{
				PlannedRelease release = plan.Packages.Single(package => package.PackageId == id);
				await Assert.That(release.DependencyIds).IsEquivalentTo(dependencies);
			}
		}
	}

	public static async Task HasReferences(ReleasePlan plan, params ReferencedPackage[] expected)
		=> await Assert.That(plan.ReferencedPackages).IsEquivalentTo(expected);

	public static async Task HasReasons(ReleasePlan plan, string packageId, params ReleaseReason[] expected)
	{
		IReadOnlyList<ReleaseReason> actual = plan.Packages.Single(package => package.PackageId == packageId).Reasons;
		using (Assert.Multiple())
		{
			await Assert.That(actual.Count).IsEqualTo(expected.Length);
			foreach (ReleaseReason reason in expected)
			{
				switch (reason)
				{
					case InitialReleaseReason:
						await Assert.That(actual.Count(item => item is InitialReleaseReason)).IsEqualTo(1);
						break;
					case DependencyReleaseReason dependency:
						await Assert.That(actual.Count(item => item is DependencyReleaseReason value && value.PackageId == dependency.PackageId)).IsEqualTo(1);
						break;
					case ConventionalCommitReason commit:
						ConventionalCommitReason direct = actual.Select(item => item is ConventionalCommitReason value ? value : null)
							.OfType<ConventionalCommitReason>().Single();
						await Assert.That(direct.Bump).IsEqualTo(commit.Bump);
						await Assert.That(direct.CommitShas).IsEquivalentTo(commit.CommitShas);
						break;
				}
			}
		}
	}

	public static async Task Precedes(ReleasePlan plan, string dependency, string consumer)
	{
		string[] ids = plan.Packages.Select(package => package.PackageId).ToArray();
		using (Assert.Multiple())
		{
			await Assert.That(Array.IndexOf(ids, dependency)).IsGreaterThanOrEqualTo(0);
			await Assert.That(Array.IndexOf(ids, consumer)).IsGreaterThan(Array.IndexOf(ids, dependency));
		}
	}
}
