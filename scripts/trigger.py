#!/usr/bin/env python3
"""Send a trigger to the WorkHealthCoach plugin (stands in for the camera while developing).

  python3 scripts/trigger.py --list
  python3 scripts/trigger.py pace_too_fast
  python3 scripts/trigger.py blink_low --severity 0.6
"""
import argparse
import json
import sys
import urllib.error
import urllib.request

BASE = "http://127.0.0.1:8787"


def call(path, payload=None):
    data = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(BASE + path, data=data, method="POST" if data else "GET",
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=3) as r:
        return json.loads(r.read())


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("trigger", nargs="?", help="trigger name, e.g. pace_too_fast")
    ap.add_argument("--severity", type=float, default=1.0, help="0.0-1.0 (default 1.0)")
    ap.add_argument("--list", action="store_true", help="list available triggers")
    a = ap.parse_args()
    try:
        if a.list or not a.trigger:
            print("\n".join(call("/triggers")["triggers"]))
        else:
            print(call("/trigger", {"trigger": a.trigger, "severity": a.severity}))
    except urllib.error.HTTPError as e:
        print("Server said:", e.read().decode(), file=sys.stderr)
        sys.exit(1)
    except (urllib.error.URLError, ConnectionError):
        print("Cannot reach the plugin on 127.0.0.1:8787. Is Logi Options+ / Logi Plugin Service running "
              "and the WorkHealthCoach plugin loaded?", file=sys.stderr)
        sys.exit(2)


if __name__ == "__main__":
    main()
