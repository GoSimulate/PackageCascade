using PackageCascade;

namespace PackageCascade.Tests.Unit;

public class ProjectInfoTests
{
	[Test]
	public async Task AssemblyNameMatchesProjectName()
	{
		await Assert.That(ProjectInfo.AssemblyName).IsEqualTo(ProjectInfo.Name);
	}
}
