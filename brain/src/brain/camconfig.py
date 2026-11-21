"""Load LogiInsight camera credentials without ever putting them in code, git or chat.

Order: real environment variables first, then the file ~/.config/workhealth/camera.env
(KEY=VALUE lines, created by the user, chmod 600, outside this repo).
"""
import os
from pathlib import Path

ENV_FILE = Path.home() / ".config" / "workhealth" / "camera.env"


def load_camera_env():
    if ENV_FILE.exists():
        for line in ENV_FILE.read_text().splitlines():
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                k, v = line.split("=", 1)
                os.environ.setdefault(k.strip(), v.strip().strip("'\""))
    return bool(os.environ.get("LOGIWEBCAM_USER") and os.environ.get("LOGIWEBCAM_PASSWORD"))
