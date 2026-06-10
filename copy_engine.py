from __future__ import annotations

import os
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Iterable


ProgressCallback = Callable[[int, int, Path, Path], None]


@dataclass(frozen=True)
class CopySummary:
    destination_count: int
    file_count: int
    copied_file_count: int


def normalize_path(path: str | Path) -> Path:
    return Path(path).expanduser().resolve()


def validate_job(source: str | Path, destinations: Iterable[str | Path]) -> tuple[Path, list[Path]]:
    source_path = normalize_path(source)
    if not source_path.is_dir():
        raise ValueError("请选择一个有效的源文件夹。")

    destination_paths: list[Path] = []
    seen: set[str] = set()
    for destination in destinations:
        destination_path = normalize_path(destination)
        key = os.path.normcase(str(destination_path))
        if key in seen:
            continue
        seen.add(key)

        if not destination_path.is_dir():
            raise ValueError(f"目标文件夹不存在：{destination_path}")
        if destination_path == source_path:
            raise ValueError("目标文件夹不能与源文件夹相同。")
        if destination_path.is_relative_to(source_path):
            raise ValueError(f"目标文件夹不能位于源文件夹内部：{destination_path}")

        final_target = destination_path / source_path.name
        if source_path.is_relative_to(final_target):
            raise ValueError(f"源文件夹不能位于最终目标目录内部：{destination_path}")

        destination_paths.append(destination_path)

    if not destination_paths:
        raise ValueError("请至少添加一个目标文件夹。")

    return source_path, destination_paths


def list_source_files(source: Path) -> list[Path]:
    return [path for path in source.rglob("*") if path.is_file()]


def copy_folder_to_many(
    source: str | Path,
    destinations: Iterable[str | Path],
    progress: ProgressCallback | None = None,
) -> CopySummary:
    source_path, destination_paths = validate_job(source, destinations)
    source_files = list_source_files(source_path)
    total_operations = len(source_files) * len(destination_paths)
    copied_operations = 0

    for destination in destination_paths:
        final_target = destination / source_path.name
        final_target.mkdir(parents=True, exist_ok=True)

        for current_root, directory_names, file_names in os.walk(source_path):
            current_path = Path(current_root)
            relative_root = current_path.relative_to(source_path)
            target_root = final_target / relative_root
            target_root.mkdir(parents=True, exist_ok=True)

            for directory_name in directory_names:
                (target_root / directory_name).mkdir(parents=True, exist_ok=True)

            for file_name in file_names:
                source_file = current_path / file_name
                target_file = target_root / file_name
                shutil.copy2(source_file, target_file)
                copied_operations += 1
                if progress is not None:
                    progress(copied_operations, total_operations, source_file, target_file)

        shutil.copystat(source_path, final_target)

    return CopySummary(
        destination_count=len(destination_paths),
        file_count=len(source_files),
        copied_file_count=copied_operations,
    )
