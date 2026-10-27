#!/usr/bin/env python3
"""Rename the current project identity in this repository."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


TEMPLATE_NAME = "TemplateProject"
REQUIRED_PATHS = (
    "src/TemplateProject",
    "src/TemplateProject.Tests.Unit",
    "src/TemplateProject.Tests.Integration",
    "src/TemplateProject.slnx",
    "infra/TemplateProject.Infrastructure",
    "infra/Pulumi.yaml",
)
EXCLUDED_PARTS = {".git", "bin", "obj"}


def _is_excluded(relative_path: Path) -> bool:
    return any(part in EXCLUDED_PARTS for part in relative_path.parts)


def _validate_name(name: str) -> None:
    if not re.fullmatch(r"[A-Z][A-Za-z0-9]*(?:\.[A-Z][A-Za-z0-9]*)*", name):
        raise ValueError("project name must contain PascalCase identifiers separated by dots")
    if name == TEMPLATE_NAME:
        raise ValueError("project already has the requested name")


def rename_project(root: Path, new_name: str) -> None:
    """Rename project references and paths under *root* after validating the template."""
    _validate_name(new_name)
    root = root.resolve()
    if not root.is_dir():
        raise ValueError(f"repository directory does not exist: {root}")

    for required in REQUIRED_PATHS:
        if not (root / required).exists():
            raise ValueError(f"not a TemplateProject repository; missing {required}")

    entries = [
        path for path in root.rglob("*")
        if not _is_excluded(path.relative_to(root)) and not path.is_symlink()
    ]
    path_moves: list[tuple[Path, Path]] = []
    for path in entries:
        relative = path.relative_to(root)
        renamed = Path(*(part.replace(TEMPLATE_NAME, new_name) for part in relative.parts))
        if renamed != relative:
            path_moves.append((relative, renamed))

    current_paths = {path.relative_to(root) for path in entries}
    moving_sources = {source for source, _ in path_moves}
    destinations = [destination for _, destination in path_moves]
    if len(destinations) != len(set(destinations)):
        raise ValueError("renaming would create duplicate paths")
    for destination in destinations:
        if destination in current_paths and destination not in moving_sources:
            raise ValueError(f"renaming would overwrite {destination}")

    # Read and prepare every text change before touching the repository.
    text_changes: list[tuple[Path, bytes]] = []
    for path in entries:
        if not path.is_file():
            continue
        data = path.read_bytes()
        try:
            text = data.decode("utf-8")
        except UnicodeDecodeError:
            continue
        if TEMPLATE_NAME in text:
            text_changes.append((path.relative_to(root), text.replace(TEMPLATE_NAME, new_name).encode("utf-8")))

    for relative, data in text_changes:
        (root / relative).write_bytes(data)

    # Move directories from the leaves upward, then files into their renamed parents.
    directory_moves = [(src, dst) for src, dst in path_moves if (root / src).is_dir()]
    file_moves = [(src, dst) for src, dst in path_moves if not (root / src).is_dir()]
    for source, destination in sorted(directory_moves, key=lambda pair: len(pair[0].parts), reverse=True):
        (root / source).rename(root / destination)

    for source, destination in sorted(file_moves, key=lambda pair: len(pair[0].parts), reverse=True):
        transformed_parent = Path(*(part.replace(TEMPLATE_NAME, new_name) for part in source.parent.parts))
        current_source = transformed_parent / source.name
        (root / current_source).rename(root / destination)

    (root / "scripts/rename_project.py").unlink()
    (root / "scripts/test_rename_project.py").unlink()
    (root / ".github/workflows/rename.yml").unlink()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project_name", help="new PascalCase project name, optionally with dotted segments")
    args = parser.parse_args(argv)
    try:
        rename_project(Path(__file__).resolve().parent.parent, args.project_name)
    except (OSError, ValueError) as error:
        print(f"rename-project: {error}", file=sys.stderr)
        return 2
    print(f"Renamed project to {args.project_name}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
