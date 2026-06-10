from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Iterable


APP_FOLDER_NAME = "skill搬运工"
CONFIG_FILE_NAME = "config.json"


def default_config_path() -> Path:
    app_data = os.environ.get("APPDATA")
    base = Path(app_data) if app_data else Path.home() / "AppData" / "Roaming"
    return base / APP_FOLDER_NAME / CONFIG_FILE_NAME


class SettingsStore:
    def __init__(self, config_path: str | Path | None = None) -> None:
        self.config_path = Path(config_path) if config_path else default_config_path()

    def load_destinations(self) -> list[str]:
        try:
            data = json.loads(self.config_path.read_text(encoding="utf-8"))
        except (FileNotFoundError, json.JSONDecodeError, OSError):
            return []

        raw_destinations = data.get("destinations", [])
        if not isinstance(raw_destinations, list):
            return []

        destinations: list[str] = []
        seen: set[str] = set()
        for item in raw_destinations:
            if not isinstance(item, str) or not item.strip():
                continue
            normalized = str(Path(item).expanduser())
            key = os.path.normcase(normalized)
            if key not in seen:
                seen.add(key)
                destinations.append(normalized)
        return destinations

    def save_destinations(self, destinations: Iterable[str]) -> None:
        self.config_path.parent.mkdir(parents=True, exist_ok=True)
        data = {"destinations": list(destinations)}
        temporary_path = self.config_path.with_suffix(".tmp")
        temporary_path.write_text(
            json.dumps(data, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        temporary_path.replace(self.config_path)
