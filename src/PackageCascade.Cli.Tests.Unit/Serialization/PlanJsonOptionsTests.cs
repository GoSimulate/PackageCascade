using System.Text.Json;
using PackageCascade.Cli.Serialization;
using PackageCascade.Public.Planning.Definition;

namespace PackageCascade.Cli.Tests.Unit.Serialization;

public sealed class PlanJsonOptionsTests
{
	[Test]
	public async Task PlanRoundTripsWithVersionAndEveryReasonCase()
	{
		ReleasePlan plan = new(
			[new PlannedRelease("Fixture", "src/Fixture/Fixture.csproj", "1.2.4-beta.1",
				[new ConventionalCommitReason(SemverBump.Minor, ["abc"]),
					new DependencyReleaseReason("Foundation")]),
				new PlannedRelease("NewPackage", "src/NewPackage/NewPackage.csproj", "0.1.0",
					[new InitialReleaseReason()])]);

		string json = JsonSerializer.Serialize(plan, PlanJsonOptions.Options);
		ReleasePlan? copy = JsonSerializer.Deserialize<ReleasePlan>(json, PlanJsonOptions.Options);

		PlannedRelease package = copy!.Packages[0];
		using (Assert.Multiple())
		{
			await Assert.That(package.ProjectPath).IsEqualTo("src/Fixture/Fixture.csproj");
			await Assert.That(package.Version).IsEqualTo("1.2.4-beta.1");
			await Assert.That(package.Reasons[0] is ConventionalCommitReason { Bump: SemverBump.Minor, CommitShas: ["abc"] }).IsTrue();
			await Assert.That(package.Reasons[1] is DependencyReleaseReason { PackageId: "Foundation" }).IsTrue();
			await Assert.That(package.PackageId).IsEqualTo("Fixture");
			await Assert.That(copy.Packages[1].Reasons.Single() is InitialReleaseReason).IsTrue();
			await Assert.That(copy.Packages[1].Version).IsEqualTo("0.1.0");
		}
	}
}
