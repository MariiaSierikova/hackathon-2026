import numpy as np
import soundfile as sf
import matplotlib.pyplot as plt

from scipy.signal import butter, sosfiltfilt, welch, find_peaks
from scipy.ndimage import gaussian_filter1d

# =====================================================
# SETTINGS
# =====================================================

files = {
    "Quiet normal": "audio_captures/quiet_good2.wav",
    "Quiet fast":   "audio_captures/quiet_fast2.wav",
    "Loud normal": "audio_captures/loud_good2.wav",
    "Loud fast":   "audio_captures/loud_fast2.wav",
}

# Use same spoken passage in both versions
NORMAL_START = 0.0
FAST_START = 0.0

NORMAL_DURATION = 10.0
FAST_DURATION = 5.0

CHANNEL = 1  # First microphone (Python index 0)

# =====================================================
# ANALYSIS
# =====================================================

def analyze_audio(path, start, duration):

    audio, fs = sf.read(path)

    if audio.ndim == 2:
        audio = audio[:, CHANNEL]

    # Crop recording
    i0 = int(start * fs)
    i1 = int((start + duration) * fs)
    audio = audio[i0:i1]

    if len(audio) < int(0.5 * fs):
        raise ValueError(f"Recording too short: {path}")

    # Overall RMS (loudness indicator)
    rms_global = np.sqrt(np.mean(audio**2))
    rms_db = 20 * np.log10(max(rms_global, 1e-12))

    # -------------------------------------------------
    # 1. Band-pass filter: 300–3000 Hz
    # -------------------------------------------------

    sos = butter(
        4, [300, 3000],
        btype="bandpass",
        fs=fs,
        output="sos"
    )

    filtered = sosfiltfilt(sos, audio)

    # -------------------------------------------------
    # 2. RMS envelope (20 ms, step 10 ms)
    # -------------------------------------------------

    win = int(0.020 * fs)
    hop = int(0.010 * fs)

    rms = np.array([
        np.sqrt(np.mean(filtered[i:i+win]**2))
        for i in range(0, len(filtered)-win+1, hop)
    ])

    envelope_fs = fs / hop
    t = np.arange(len(rms)) / envelope_fs

    # Smooth envelope (~20 ms)
    envelope = gaussian_filter1d(rms, sigma=2)

    # Robust normalization
    reference = np.percentile(envelope, 75)

    if reference > 0:
        envelope = envelope / reference

    # Limit influence of exceptionally loud transients
    envelope = np.clip(
        envelope, 0, np.percentile(envelope, 95)
    )

    # -------------------------------------------------
    # 3. Approximate syllable nuclei detection
    # -------------------------------------------------

    peaks, _ = find_peaks(
        envelope,
        prominence=0.20,
        distance=int(0.09 * envelope_fs)
    )

    peak_rate = len(peaks) / (len(audio) / fs)

    # -------------------------------------------------
    # 4. Amplitude modulation spectrum
    # -------------------------------------------------

    # Remove constant component
    modulation = envelope - np.mean(envelope)

    frequencies, power = welch(
        modulation,
        fs=envelope_fs,
        nperseg=min(512, len(modulation))
    )

    # Search for speech rhythm between 1.5 and 9 Hz
    mask = (frequencies >= 1.5) & (frequencies <= 9)

    freq_selected = frequencies[mask]
    power_selected = power[mask]

    dominant_rhythm = freq_selected[np.argmax(power_selected)]

    return {
        "rms_db": rms_db,
        "peaks": len(peaks),
        "peak_rate": peak_rate,
        "rhythm": dominant_rhythm,
        "time": t,
        "envelope": envelope,
        "peak_indices": peaks,
        "freq": freq_selected,
        "power": power_selected,
    }


# =====================================================
# RUN ANALYSIS
# =====================================================

results = {}

for name, path in files.items():

    fast = "fast" in name.lower()

    start = FAST_START if fast else NORMAL_START
    duration = FAST_DURATION if fast else NORMAL_DURATION

    results[name] = analyze_audio(path, start, duration)


# =====================================================
# PRINT RESULTS
# =====================================================

print("\nRESULTS")
print("-" * 85)

for name, r in results.items():
    print(f"{name:15s} | "
        f"RMS: {r['rms_db']:6.1f} dBFS | "
        f"Peaks: {r['peaks']:3d} | "
        f"Peaks/s: {r['peak_rate']:5.2f} | "
        f"Rhythm: {r['rhythm']:5.2f} Hz"
    )


# =====================================================
# PLOTS
# =====================================================

fig, axes = plt.subplots(4, 2, figsize=(13, 12))

for row, (name, r) in enumerate(results.items()):

    # Speech envelope
    ax = axes[row, 0]

    ax.plot(r["time"], r["envelope"])
    ax.plot(
        r["time"][r["peak_indices"]],
        r["envelope"][r["peak_indices"]],
        "rx"
    )

    ax.set_title(
        f"{name}: {r['peak_rate']:.2f} peaks/s"
    )
    ax.set_xlabel("Time (s)")
    ax.set_ylabel("Normalized amplitude")
    ax.grid(alpha=0.3)

    # Modulation spectrum
    ax = axes[row, 1]

    ax.plot(r["freq"], r["power"])
    ax.axvline(
        r["rhythm"],
        color="red",
        linestyle="--",
        label=f"{r['rhythm']:.2f} Hz"
    )

    ax.set_title(f"{name}: modulation spectrum")
    ax.set_xlabel("Modulation frequency (Hz)")
    ax.set_ylabel("Power")
    ax.legend()
    ax.grid(alpha=0.3)

plt.tight_layout()
plt.show()