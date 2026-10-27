using PackageCascade;

namespace PackageCascade.Tests.Integration;

public class ProjectInfoIntegrationTests
{
	[Test]
	public async Task PublicProjectNameIsAvailableToConsumers()
	{
		await Assert.That(ProjectInfo.Name).IsEqualTo("PackageCascade");
	}
}
