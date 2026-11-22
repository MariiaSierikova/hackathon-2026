"""Speech loudness from the microphone: "is the speaker too quiet?" with hysteresis.

Based on the teammate's logiinsight_api/newscripts/speech_loudness_flagger.py (5 s window, threshold -25 dBFS), with one fix:
the level is measured ONLY over frames where someone is speaking. The original averaged the whole window including
silence, so every pause would have looked like "too quiet".
"""
import numpy as np
from scipy.signal import butter, sosfiltfilt

MIC_CHANNEL = 0
WINDOW_SEC = 5.0
FRAME_SEC = 0.030
MIN_ENERGY = 0.003            # same "someone is speaking" level as the pace code (band-passed frame RMS)
MIN_SPEECH_SEC = 1.0          # need at least this much speech in the window, else "no verdict"
QUIET_DBFS = -25.0            # teammate's threshold: speech quieter than this = too quiet
OK_DBFS = -22.0               # back above this = fine again (3 dB hysteresis stops flicker)

_sos = {}


def estimate_loudness(audio, fs=48000):
    """Returns the speech level in dBFS over the last WINDOW_SEC seconds, or None if there is not enough speech to judge."""
    audio = np.asarray(audio)
    if audio.ndim == 2:
        audio = audio[:, min(MIC_CHANNEL, audio.shape[1] - 1)]
    audio = audio.astype(np.float64) / 32768.0 if audio.dtype.kind in "iu" else audio.astype(np.float64)
    audio = audio[-int(WINDOW_SEC * fs):]
    frame = int(FRAME_SEC * fs)
    n = len(audio) // frame
    if n < int(MIN_SPEECH_SEC / FRAME_SEC):
        return None

    if fs not in _sos:
        _sos[fs] = butter(4, [300, 3000], btype="bandpass", fs=fs, output="sos")
    raw = audio[:n * frame].reshape(n, frame)
    band = sosfiltfilt(_sos[fs], audio[:n * frame]).reshape(n, frame)
    speaking = np.sqrt(np.mean(band ** 2, axis=1)) >= MIN_ENERGY
    if speaking.sum() * FRAME_SEC < MIN_SPEECH_SEC:
        return None
    rms = np.sqrt(np.mean(raw[speaking] ** 2))
    return float(20 * np.log10(max(rms, 1e-12)))


class LoudnessState:
    """True while the speaker is 'too quiet'. No verdict (None / not speaking) ends the warning."""

    def __init__(self, quiet_db=QUIET_DBFS, ok_db=OK_DBFS):
        self.quiet_db, self.ok_db = quiet_db, ok_db
        self.quiet = False

    def update(self, dbfs, speaking=True):
        if dbfs is None or not speaking:
            self.quiet = False
        elif dbfs < self.quiet_db:
            self.quiet = True
        elif dbfs > self.ok_db:
            self.quiet = False
        return self.quiet

    def severity(self, dbfs):
        """0.5 at the threshold, 1.0 eight dB below it."""
        return max(0.5, min(1.0, 0.5 + 0.5 * (self.quiet_db - dbfs) / 8.0))
