"""Resolve current, explicitly catalogued GHX files (never unlisted or archived copies)."""
from pathlib import Path


def current_definitions(root: Path, catalog: list[dict]) -> list[Path]:
    paths = []
    for row in catalog:
        names = ([row['definition']] if row.get('definition') else []) + row.get('additionalDefinitions', [])
        for name in names:
            path = (root / name).resolve()
            if not path.is_relative_to((root / row['id']).resolve()) or path.suffix.lower() != '.ghx':
                raise ValueError(f'Invalid current definition for {row["id"]}: {name}')
            if path in paths:
                raise ValueError(f'Duplicate current definition: {name}')
            paths.append(path)
    return paths
