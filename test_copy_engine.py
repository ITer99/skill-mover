from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from copy_engine import copy_folder_to_many, validate_job
from settings_store import SettingsStore


class CopyEngineTests(unittest.TestCase):
    def test_copies_source_folder_and_nested_contents_to_many_destinations(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            source = root / "my-skill"
            nested = source / "scripts"
            nested.mkdir(parents=True)
            (source / "SKILL.md").write_text("hello", encoding="utf-8")
            (nested / "tool.py").write_text("print('ok')", encoding="utf-8")
            empty = source / "assets"
            empty.mkdir()

            destination_a = root / "target-a"
            destination_b = root / "target-b"
            destination_a.mkdir()
            destination_b.mkdir()

            summary = copy_folder_to_many(source, [destination_a, destination_b])

            self.assertEqual(summary.destination_count, 2)
            self.assertEqual(summary.file_count, 2)
            self.assertEqual(summary.copied_file_count, 4)
            for destination in (destination_a, destination_b):
                copied = destination / "my-skill"
                self.assertEqual((copied / "SKILL.md").read_text(encoding="utf-8"), "hello")
                self.assertTrue((copied / "scripts" / "tool.py").is_file())
                self.assertTrue((copied / "assets").is_dir())

    def test_existing_files_are_overwritten_and_unrelated_files_remain(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            source = root / "source"
            source.mkdir()
            (source / "same.txt").write_text("new", encoding="utf-8")

            destination = root / "destination"
            existing = destination / "source"
            existing.mkdir(parents=True)
            (existing / "same.txt").write_text("old", encoding="utf-8")
            (existing / "keep.txt").write_text("keep", encoding="utf-8")

            copy_folder_to_many(source, [destination])

            self.assertEqual((existing / "same.txt").read_text(encoding="utf-8"), "new")
            self.assertEqual((existing / "keep.txt").read_text(encoding="utf-8"), "keep")

    def test_rejects_destination_inside_source(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            source = Path(temp_dir) / "source"
            destination = source / "nested"
            destination.mkdir(parents=True)

            with self.assertRaisesRegex(ValueError, "不能位于源文件夹内部"):
                validate_job(source, [destination])


class SettingsStoreTests(unittest.TestCase):
    def test_saves_and_restores_destinations_in_order(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            config_path = Path(temp_dir) / "设置" / "config.json"
            store = SettingsStore(config_path)
            destinations = [r"G:\技能目录", r"D:\backup"]

            store.save_destinations(destinations)

            self.assertEqual(store.load_destinations(), destinations)

    def test_ignores_duplicate_and_invalid_saved_values(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            config_path = Path(temp_dir) / "config.json"
            config_path.write_text(
                '{"destinations": ["D:\\\\skills", "", "D:\\\\skills", 123]}',
                encoding="utf-8",
            )

            self.assertEqual(SettingsStore(config_path).load_destinations(), [r"D:\skills"])


if __name__ == "__main__":
    unittest.main()
