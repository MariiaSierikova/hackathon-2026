import numpy as np
import matplotlib.pyplot as plt
import soundfile as sf
from scipy.signal import find_peaks, stft
from scipy.ndimage import gaussian_filter1d

# =============================================
# FILE PATHS
# =============================================

files = {
    "Slow": r"C:\LauzHack\logiinsight_api\audio_captures\loud_good.wav",
    "Fast": r"C:\LauzHack\logiinsight_api\audio_captures\sample_livespeech.wav"
}

def analyze_audio(path):
    audio, fs = sf.read(path)

    # Use microphone 1 (channel 1)
    if audio.ndim == 2:
        audio = audio[:, 0]

    # Short-time RMS: 20 ms windows, 10 ms step
    win = int(0.020 * fs)
    hop = int(0.010 * fs)

    rms = np.array([
        np.sqrt(np.mean(audio[i:i+win]**2))
        for i in range(0, len(audio)-win+1, hop)
    ])

    t = np.arange(len(rms)) * hop / fs

    # Smooth RMS
    rms_smooth = gaussian_filter1d(rms, sigma=2)

    # Detect syllable-like energy peaks
    peaks, _ = find_peaks(
        rms_smooth,
        prominence=0.05 * np.percentile(rms_smooth, 75),
        distance=int(0.05 / (hop/fs))
    )

    duration = len(audio) / fs
    peak_rate = len(peaks) / duration

    # Spectral flux
    _, _, Z = stft(audio, fs, nperseg=1024, noverlap=512)
    magnitude = np.abs(Z)
    magnitude /= np.maximum(np.linalg.norm(magnitude, axis=0), 1e-12)
    flux = np.sum(np.maximum(np.diff(magnitude, axis=1), 0)**2, axis=0)
    mean_flux = np.mean(flux)

    return {
        "duration": duration,
        "rms": np.sqrt(np.mean(audio**2)),
        "flux": mean_flux,
        "peak_rate": peak_rate,
        "time": t,
        "envelope": rms_smooth,
        "peaks": peaks
    }

results = {name: analyze_audio(path) for name, path in files.items()}

# =============================================
# PRINT RESULTS
# =============================================

for name, r in results.items():
    print(f"\n{name}")
    print(f"Duration: {r['duration']:.2f} s")
    print(f"RMS: {r['rms']:.4f}")
    print(f"Spectral flux: {r['flux']:.5f}")
    print(f"Energy peaks/s: {r['peak_rate']:.2f}")

# =============================================
# PLOT
# =============================================

fig, axes = plt.subplots(2, 1, figsize=(12, 7))

for ax, (name, r) in zip(axes, results.items()):
    ax.plot(r["time"], r["envelope"], label="RMS envelope")
    ax.plot(
        r["time"][r["peaks"]],
        r["envelope"][r["peaks"]],
        "rx", label="Detected peaks"
    )
    ax.set_title(f"{name} — {r['peak_rate']:.2f} peaks/s")
    ax.set_xlabel("Time (s)")
    ax.set_ylabel("RMS amplitude")
    ax.legend()
    ax.grid(alpha=0.3)

plt.tight_layout()
plt.show()

# =============================================
# AUTOMATIC COMPARISON
# =============================================

names = list(results)
a, b = names

if results[a]["peak_rate"] > results[b]["peak_rate"]:
    print(f"\nEstimated faster speech: {a}")
elif results[b]["peak_rate"] > results[a]["peak_rate"]:
    print(f"\nEstimated faster speech: {b}")
else:
    print("\nNo clear difference in estimated speech rate.")