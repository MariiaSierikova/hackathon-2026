"""Speech pace from the microphone: syllables per second, and "is the speaker too fast?" with hysteresis.

The detection (band-pass 300-3000 Hz, 30 ms RMS frames, high-passed energy envelope, positive zero crossings) is the
teammate's, taken UNCHANGED from logiinsight_api/speech_flagger.py; only the hardware/GUI parts were moved out so the
same logic can run inside the single launcher (no extra window).
"""
import numpy as np
from scipy.signal import butter, lfilter, sosfiltfilt

# ---- teammate's settings (logiinsight_api/speech_flagger.py) ----
MIC_CHANNEL = 0          # microphone 1
WINDOW_SEC = 4.0
RATE_THRESHOLD = 5.0     # syllables/s: from here on "SLOW DOWN"
RATE_RESET = 4.3         # back below this: pace is fine again
FRAME_SEC = 0.030
HP_ALPHA = 0.9
MIN_INTERVAL = 0.070
MIN_ENERGY = 0.003

SYLLABLES_PER_WORD = 1.5   # rough, only to show an approximate "words per minute" on the Keypad

_sos_cache = {}


def _sos(fs):
    if fs not in _sos_cache:
        _sos_cache[fs] = butter(4, [300, 3000], btype="bandpass", fs=fs, output="sos")
    return _sos_cache[fs]


def estimate_speech_rate(audio, fs=48000):
    """Returns (syllables_per_second, speaking) for the latest WINDOW_SEC seconds of audio."""
    audio = np.asarray(audio)
    if audio.ndim == 2:
        audio = audio[:, min(MIC_CHANNEL, audio.shape[1] - 1)]
    if audio.dtype.kind in "iu":
        audio = audio.astype(np.float64) / 32768.0
    else:
        audio = audio.astype(np.float64)

    audio = audio[-int(WINDOW_SEC * fs):]
    if len(audio) < int(WINDOW_SEC * fs):
        return 0.0, False

    filtered = sosfiltfilt(_sos(fs), audio)

    frame_samples = int(FRAME_SEC * fs)
    n = len(filtered) // frame_samples
    frames = filtered[:n * frame_samples].reshape(n, frame_samples)
    rms = np.sqrt(np.mean(frames ** 2, axis=1))

    energy = lfilter([0.3], [1, -0.7], rms)
    hp = lfilter([HP_ALPHA, -HP_ALPHA], [1, -HP_ALPHA], rms)

    crossings = np.where((hp[:-1] <= 0) & (hp[1:] > 0) & (energy[1:] >= MIN_ENERGY))[0] + 1

    times = crossings * FRAME_SEC
    accepted = []
    last_time = -np.inf
    for t in times:
        if t - last_time >= MIN_INTERVAL:
            accepted.append(t)
            last_time = t

    rate = len(accepted) / WINDOW_SEC
    recent_frames = max(1, int(0.5 / FRAME_SEC))
    speaking = np.mean(energy[-recent_frames:]) >= MIN_ENERGY
    return rate, bool(speaking)


class PaceState:
    """Hysteresis from the teammate's update(): stops the warning from flickering around the threshold."""

    def __init__(self, fast_rate=RATE_THRESHOLD, ok_rate=RATE_RESET):
        self.fast_rate, self.ok_rate = fast_rate, ok_rate
        self.fast = False

    def update(self, rate, speaking):
        """Returns True while the speaker is 'too fast'."""
        if not speaking:
            self.fast = False
        elif rate >= self.fast_rate:
            self.fast = True
        elif rate < self.ok_rate:
            self.fast = False
        return self.fast

    def severity(self, rate):
        """0.5 at the fast threshold, 1.0 two syllables/s above it."""
        return max(0.5, min(1.0, 0.5 + 0.5 * (rate - self.fast_rate) / 2.0))


def syllables_to_wpm(rate):
    return rate * 60.0 / SYLLABLES_PER_WORD


class MicSource:
    """Live microphone -> (rate, speaking). Prefers the LogiInsight camera microphone, else the computer's default input."""

    def __init__(self, mic=None, say=print):
        from audio_utils import AudioRingRecorder, find_audio_device, list_audio_devices

        device = None
        try:
            device = find_audio_device(mic, min_channels=2 if mic is None else 1)
            say(f"microphone: {device['name']} ({device.get('hostapi', '?')}, {device['max_input_channels']} ch)")
        except Exception as e:
            if mic is not None:
                raise
            say(f"camera microphone not found ({str(e).splitlines()[0]}); using the computer's default microphone")
            import sounddevice as sd
            default_in = sd.default.device[0]
            for d in list_audio_devices():
                if d["index"] == default_in:
                    device = d
            if device is None:
                raise
            say(f"microphone: {device['name']}")

        self.fs = int(device.get("default_samplerate") or 48000)
        if "LVNext" in device["name"] or "Logi" in device["name"]:
            self.fs = 48000
        channels = max(1, min(2, int(device["max_input_channels"])))
        self.recorder = AudioRingRecorder(device_info=device, samplerate=self.fs, channels=channels, seconds=WINDOW_SEC + 1)
        self.recorder.start()
        self.loudness = None       # speech level in dBFS of the latest read(), None = not enough speech to judge

    def read(self):
        snapshot = self.recorder.snapshot()
        audio = snapshot[0] if isinstance(snapshot, tuple) else snapshot
        from brain.audio.loudness import estimate_loudness
        self.loudness = estimate_loudness(audio, self.fs)
        return estimate_speech_rate(audio, self.fs)

    def close(self):
        try:
            self.recorder.stop()
        except Exception:
            pass


class FakeSource:
    """--simulate: a speaker who talks normally, then too fast, then normally again, then stops."""

    # (start second, syllables per second, speaking)
    SCHEDULE = [(0, 4.0, True), (26, 6.2, True), (42, 3.8, True), (47, 0.0, False)]
    # (start second, speech level dBFS): normal, TOO QUIET 8-22 s, normal again
    LOUDNESS = [(0, -18.0), (8, -33.0), (22, -18.0)]
    DURATION = 50

    def __init__(self):
        import time
        self.t0 = time.time()
        self._time = time
        self.loudness = None

    @property
    def finished(self):
        return self._time.time() - self.t0 > self.DURATION

    def read(self):
        t = self._time.time() - self.t0
        rate, speaking = 4.0, True
        self.loudness = next(db for start, db in reversed(self.LOUDNESS) if t >= start) if t < 47 else None
        for start, r, sp in self.SCHEDULE:
            if t >= start:
                rate, speaking = r, sp
        import random
        return (max(0.0, rate + random.uniform(-0.2, 0.2)) if speaking else 0.0), speaking

    def close(self):
        pass
