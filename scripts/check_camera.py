#!/usr/bin/env python3
"""Run Logitech's connection checker with credentials from ~/.config/workhealth/camera.env."""
import runpy
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(root / "brain" / "src"))
sys.path.insert(0, str(root / "brain" / "vendor" / "logiinsight"))
from brain.camconfig import load_camera_env  # noqa: E402

if not load_camera_env():
    sys.exit("Camera credentials not found. See docs/CAMERA_TEST.md step 3.")
runpy.run_path(str(root / "brain" / "vendor" / "logiinsight" / "check_connection.py"), run_name="__main__")
