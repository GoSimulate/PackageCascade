from pathlib import Path
import os
import sys
import xml.etree.ElementTree as ET
import zipfile


version = sys.argv[1]
package_id = "PackageCascade"
package_directory = Path("artifacts/packages")
expected_packages = {
    f"{package_id}.{version}.nupkg",
    f"{package_id}.{version}.snupkg",
}
actual_packages = {path.name for path in package_directory.glob("*.nupkg")}
actual_packages.update(path.name for path in package_directory.glob("*.snupkg"))
assert actual_packages == expected_packages, (actual_packages, expected_packages)

for extension in ("nupkg", "snupkg"):
    with zipfile.ZipFile(package_directory / f"{package_id}.{version}.{extension}") as package:
        metadata = ET.fromstring(package.read(f"{package_id}.nuspec")).find("{*}metadata")
        assert metadata is not None
        assert metadata.find("{*}id").text == package_id
        assert metadata.find("{*}version").text == version
        repository = metadata.find("{*}repository")
        assert repository is not None
        assert repository.attrib["type"] == "git"
        assert repository.attrib["url"].removesuffix(".git") == "https://github.com/GoSimulate/PackageCascade"
        if "GITHUB_SHA" in os.environ:
            assert repository.attrib["commit"] == os.environ["GITHUB_SHA"]

print(f"Verified {package_id} package and symbols at {version}")
