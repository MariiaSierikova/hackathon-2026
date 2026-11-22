"""Reads the settings page's numbers (GET /ui-state) so the detectors follow the sliders, and writes the calibrated voice floor back.

The page stores one JSON document: features.<id>.params.<key>. We use only what a detector can act on:
  speed.fast   words/minute above which you are "too fast"      -> syllables/s  (x 1.5 syllables per word / 60 s)
  volume.quiet voice floor in "dB" (page scale 30-80)           -> dBFS         (page dB - 75, so the page default 50 = -25 dBFS)
Nothing here raises: if the plugin does not answer or the page was never opened, the detectors keep their built-in defaults.
"""
import json
import statistics
import urllib.request

SYLLABLES_PER_WORD = 1.5          # same rough conversion as brain/audio/pace.py
PAGE_DB_OFFSET = 75.0             # dBFS = page dB - 75 (page default 50 dB -> -25 dBFS, the tested threshold)


def wpm_to_syllables(wpm):
    return float(wpm) * SYLLABLES_PER_WORD / 60.0


def page_db_to_dbfs(db):
    return float(db) - PAGE_DB_OFFSET


def dbfs_to_page_db(dbfs):
    return float(dbfs) + PAGE_DB_OFFSET


def _num(doc, *path):
    node = doc
    for p in path:
        if not isinstance(node, dict) or p not in node:
            return None
        node = node[p]
    return float(node) if isinstance(node, (int, float)) and not isinstance(node, bool) else None


def page_thresholds(coach):
    """{'fast_wpm': .., 'quiet_db': ..} with only the keys the page has. {} if the plugin does not answer."""
    doc = coach.get_json("/ui-state", timeout_s=0.6)
    if not isinstance(doc, dict):
        return {}
    out = {}
    fast = _num(doc, "features", "speed", "params", "fast")
    quiet = _num(doc, "features", "volume", "params", "quiet")
    if fast is not None and 40 <= fast <= 400:
        out["fast_wpm"] = fast
    if quiet is not None and 0 <= quiet <= 120:
        out["quiet_db"] = quiet
    return out


def set_page_param(coach, feature, key, value):
    """Writes features.<feature>.params.<key> = value into the page's document (page and Keypad pick it up within seconds)."""
    doc = coach.get_json("/ui-state", timeout_s=1.0)
    if not isinstance(doc, dict) or not isinstance(doc.get("features"), dict):
        return False
    f = doc["features"].setdefault(feature, {})
    f.setdefault("params", {})[key] = value
    try:
        req = urllib.request.Request(coach.base + "/ui-state", data=json.dumps(doc).encode(), method="POST",
                                     headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=1.5) as r:
            return json.loads(r.read()).get("ok") is True
    except Exception as e:
        coach.last_error = e
        return False


def calibrate_volume(coach, source, say=print, seconds=10.0, sleep=None, margin_db=10.0):
    """Measures your normal speaking level for `seconds` and puts the page's voice floor `margin_db` below it. Returns the page dB or None."""
    import time
    sleep = sleep or time.sleep
    say(f"Calibration: speak NORMALLY for {seconds:.0f} seconds (read something aloud) ...")
    levels, t = [], 0.0
    while t < seconds:
        source.read()
        if source.loudness is not None:
            levels.append(source.loudness)
        sleep(0.5)
        t += 0.5
    if len(levels) < 5:
        say("Calibration failed: not enough speech heard. Speak louder/closer, check the microphone number, and try again.")
        return None
    normal = statistics.median(levels)
    page_db = int(round(max(30.0, min(80.0, dbfs_to_page_db(normal - margin_db)))))
    say(f"Your normal level is {normal:.1f} dBFS. Voice floor = {margin_db:.0f} dB below that: {page_db} dB on the page "
        f"({page_db_to_dbfs(page_db):.1f} dBFS).")
    if set_page_param(coach, "volume", "quiet", page_db):
        say("Written to the settings page: the 'Volume floor' slider moved. The detector uses it right away.")
    else:
        say("Could not write to the settings page (plugin not answering, or the page was never opened). "
            f"Use --quiet-db {page_db_to_dbfs(page_db):.1f} instead.")
    return page_db
