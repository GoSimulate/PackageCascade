from __future__ import annotations

import tempfile
import unittest
import shutil
import subprocess
import sys
from pathlib import Path

from rename_project import rename_project


class RenameProjectTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self._write("src/TemplateProject/TemplateProject.csproj", "<Project>TemplateProject</Project>\r\n")
        self._write("src/TemplateProject.Tests.Unit/TemplateProject.Tests.Unit.csproj", "TemplateProject.Tests.Unit\n")
        self._write("src/TemplateProject.Tests.Integration/TemplateProject.Tests.Integration.csproj", "TemplateProject.Tests.Integration\n")
        self._write("src/TemplateProject.slnx", "TemplateProject.slnx\n")
        self._write("infra/TemplateProject.Infrastructure/TemplateProject.Infrastructure.csproj", "TemplateProject.Infrastructure\n")
        self._write("infra/Pulumi.yaml", "name: TemplateProject\r\n")
        self._write(".github/workflows/build.yml", "name: TemplateProject CI\nrun: TemplateProject.Tests.Unit\n")
        self._write(".github/workflows/rename.yml", "name: Rename script\n")
        self._write("README.md", "# TemplateProject\nCopyright GoSimulate\n")
        self._write("src/TemplateProject/assets.bin", b"\x00TemplateProject\xff")
        self._write("src/TemplateProject/obj/TemplateProject.cache", "TemplateProject")
        self._write(".git/config", "TemplateProject")
        (self.root / "scripts").mkdir()
        shutil.copy2(Path(__file__).with_name("rename_project.py"), self.root / "scripts/rename_project.py")
        shutil.copy2(Path(__file__), self.root / "scripts/test_rename_project.py")

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def _write(self, relative: str, content: str | bytes) -> None:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content.encode("utf-8") if isinstance(content, str) else content)

    def test_renames_paths_and_text_without_touching_binary_or_excluded_directories(self) -> None:
        binary = (self.root / "src/TemplateProject/assets.bin").read_bytes()
        ignored_git_file = self.root / ".git/config"

        subprocess.run(
            [sys.executable, str(self.root / "scripts/rename_project.py"), "RotorDesign"],
            check=True,
            capture_output=True,
            text=True,
        )

        self.assertTrue((self.root / "src/RotorDesign/RotorDesign.csproj").is_file())
        self.assertTrue((self.root / "src/RotorDesign.Tests.Unit/RotorDesign.Tests.Unit.csproj").is_file())
        self.assertTrue((self.root / "src/RotorDesign.Tests.Integration/RotorDesign.Tests.Integration.csproj").is_file())
        self.assertTrue((self.root / "src/RotorDesign.slnx").is_file())
        self.assertTrue((self.root / "infra/RotorDesign.Infrastructure/RotorDesign.Infrastructure.csproj").is_file())
        self.assertEqual((self.root / "infra/Pulumi.yaml").read_bytes(), b"name: RotorDesign\r\n")
        self.assertIn("RotorDesign.Tests.Unit", (self.root / ".github/workflows/build.yml").read_text())
        self.assertEqual((self.root / "README.md").read_text(), "# RotorDesign\nCopyright GoSimulate\n")
        self.assertEqual((self.root / "src/RotorDesign/assets.bin").read_bytes(), binary)
        self.assertEqual((self.root / "src/RotorDesign/obj/TemplateProject.cache").read_text(), "TemplateProject")
        self.assertEqual(ignored_git_file.read_text(), "TemplateProject")
        self.assertFalse((self.root / "scripts/rename_project.py").exists())
        self.assertFalse((self.root / "scripts/test_rename_project.py").exists())
        self.assertFalse((self.root / ".github/workflows/rename.yml").exists())

        stale_locations = []
        for path in self.root.rglob("*"):
            relative = path.relative_to(self.root)
            if any(part in {".git", "bin", "obj"} for part in relative.parts):
                continue
            if "TemplateProject" in str(relative):
                stale_locations.append(str(relative))
            if path.is_file():
                try:
                    text = path.read_bytes().decode("utf-8")
                    if "TemplateProject" in text:
                        stale_locations.append(str(relative))
                except (OSError, UnicodeDecodeError):
                    pass
        self.assertEqual(stale_locations, [])

    def test_rejects_invalid_name_before_changes(self) -> None:
        original = (self.root / "README.md").read_bytes()
        for invalid in ("rotorDesign", "Rotor-Design", "Rotor_Design", "TemplateProject",
                        ".Motor", "Motor.", "Motor..Core", "Motor.core"):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                rename_project(self.root, invalid)
        self.assertEqual((self.root / "README.md").read_bytes(), original)

    def test_renames_to_dot_separated_project_name(self) -> None:
        rename_project(self.root, "GoSim.Motor.Core")

        self.assertTrue((self.root / "src/GoSim.Motor.Core/GoSim.Motor.Core.csproj").is_file())
        self.assertTrue((self.root / "src/GoSim.Motor.Core.slnx").is_file())
        self.assertTrue((self.root / "infra/GoSim.Motor.Core.Infrastructure/GoSim.Motor.Core.Infrastructure.csproj").is_file())
        self.assertEqual((self.root / "infra/Pulumi.yaml").read_bytes(), b"name: GoSim.Motor.Core\r\n")
        self.assertIn("GoSim.Motor.Core.Tests.Unit", (self.root / ".github/workflows/build.yml").read_text())

    def test_rejects_non_template_directory(self) -> None:
        other_root = self.root / "other"
        other_root.mkdir()
        with self.assertRaisesRegex(ValueError, "not a TemplateProject repository"):
            rename_project(other_root, "RotorDesign")


if __name__ == "__main__":
    unittest.main()
