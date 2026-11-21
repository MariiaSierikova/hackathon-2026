import tkinter as tk
import numpy as np

from scipy.signal import butter, sosfiltfilt, lfilter

# Logitech SDK
from audio_utils import find_audio_device, AudioRingRecorder

# =====================================================
# SETTINGS
# =====================================================

DEVICE = 11           # Set Logitech device index if needed
FS = 48000
CHANNELS = 2
MIC_CHANNEL = 0         # Microphone 1

WINDOW_SEC = 4.0
UPDATE_MS = 500         # Update every 0.5 seconds

RATE_THRESHOLD = 5    # Show SLOW DOWN
RATE_RESET = 4.3        # Hide warning below this rate

FRAME_SEC = 0.030
HP_ALPHA = 0.9
MIN_INTERVAL = 0.070
MIN_ENERGY = 0.003

# =====================================================
# LOUDNESS SETTINGS
# =====================================================

LOUDNESS_WINDOW_SEC = 5.0
LOUDNESS_THRESHOLD = -25.0  # dBFS

loudness_triggered = False

# =====================================================
# 1. CONNECT TO LOGITECH MICROPHONE
# =====================================================

device = find_audio_device(DEVICE, min_channels=CHANNELS)

print("Logitech microphone found:")
print(device)

# Logitech SDK continuous recorder
recorder = AudioRingRecorder(
    device_info=device,
    samplerate=FS,
    channels=CHANNELS,
    seconds=WINDOW_SEC + 1
)

recorder.start()

# =====================================================
# 2. SPEECH-SPEED ANALYSIS
# =====================================================

sos = butter(
    4,
    [300, 3000],
    btype="bandpass",
    fs=FS,
    output="sos"
)


def estimate_speech_rate(audio):

    # Convert PCM to floating point
    audio = np.asarray(audio)

    if audio.ndim == 2:
        audio = audio[:, MIC_CHANNEL]

    if audio.dtype.kind in "iu":
        audio = audio.astype(np.float64) / 32768.0
    else:
        audio = audio.astype(np.float64)

    # Keep the latest 4 seconds
    audio = audio[-int(WINDOW_SEC * FS):]

    if len(audio) < int(WINDOW_SEC * FS):
        return 0.0, False

    # Band-pass filter
    filtered = sosfiltfilt(sos, audio)

    # RMS every 30 ms
    frame_samples = int(FRAME_SEC * FS)

    n = len(filtered) // frame_samples

    frames = filtered[:n * frame_samples].reshape(
        n, frame_samples
    )

    rms = np.sqrt(np.mean(frames**2, axis=1))

    # Smoothed energy for speech activity
    energy = lfilter([0.3], [1, -0.7], rms)

    # High-pass energy envelope
    hp = lfilter(
        [HP_ALPHA, -HP_ALPHA],
        [1, -HP_ALPHA],
        rms
    )

    # Positive zero crossings
    crossings = np.where(
        (hp[:-1] <= 0) &
        (hp[1:] > 0) &
        (energy[1:] >= MIN_ENERGY)
    )[0] + 1

    # Minimum interval between events
    times = crossings * FRAME_SEC

    accepted = []
    last_time = -np.inf

    for t in times:
        if t - last_time >= MIN_INTERVAL:
            accepted.append(t)
            last_time = t

    rate = len(accepted) / WINDOW_SEC

    # Detect whether someone is currently speaking
    recent_frames = max(1, int(0.5 / FRAME_SEC))

    speaking = (
        np.mean(energy[-recent_frames:]) >= MIN_ENERGY
    )

    return rate, speaking

# =====================================================
# LOUDNESS ANALYSIS
# =====================================================

def estimate_loudness(audio):

    audio = np.asarray(audio)

    # Select microphone channel
    if audio.ndim == 2:
        audio = audio[:, MIC_CHANNEL]

    # Convert to float
    if audio.dtype.kind in "iu":
        audio = audio.astype(np.float64) / 32768.0
    else:
        audio = audio.astype(np.float64)

    # Keep last 5 seconds
    n_samples = int(LOUDNESS_WINDOW_SEC * FS)
    audio = audio[-n_samples:]

    # Check if enough data are available
    if len(audio) < n_samples:
        return None

    # Calculate RMS
    rms = np.sqrt(np.mean(audio**2))

    # Convert to dBFS
    rms_dbfs = 20 * np.log10(max(rms, 1e-12))

    return rms_dbfs
# =====================================================
# 3. WINDOWS DISPLAY
# =====================================================

root = tk.Tk()
root.title("Logitech Speech Speed Monitor")
root.geometry("480x260")
root.configure(bg="#202020")
root.attributes("-topmost", True)

warning = tk.Label(
    root,
    text="Listening...",
    font=("Arial", 32, "bold"),
    fg="white",
    bg="#202020"
)
warning.pack(pady=35)

rate_label = tk.Label(
    root,
    text="Estimated rate: --",
    font=("Arial", 18),
    fg="white",
    bg="#202020"
)
rate_label.pack()

status_label = tk.Label(
    root,
    text="Logitech microphone connected",
    font=("Arial", 11),
    fg="lightgray",
    bg="#202020"
)
status_label.pack(pady=12)

warning_active = False


# =====================================================
# 4. LIVE LOOP
# =====================================================

def update():

    global warning_active, loudness_triggered

    try:
        snapshot = recorder.snapshot()

        # Some SDK versions may return metadata
        # alongside the audio array.
        audio = (
            snapshot[0]
            if isinstance(snapshot, tuple)
            else snapshot
        )

        rate, speaking = estimate_speech_rate(audio)
	# =====================================================
	# LOUDNESS TRIGGER
	# =====================================================

	loudness_dbfs = estimate_loudness(audio)

	if loudness_dbfs is not None:

    		new_loudness_trigger = (
        		loudness_dbfs < LOUDNESS_THRESHOLD
    		)

    		if new_loudness_trigger != loudness_triggered:

        		loudness_triggered = new_loudness_trigger

        		print(
            		f"Loudness trigger = {loudness_triggered}, "
           	 	f"RMS = {loudness_dbfs:.1f} dBFS"
        		)

        # Hysteresis prevents warning flickering
        if not speaking:
            warning_active = False
        elif rate >= RATE_THRESHOLD:
            warning_active = True
        elif rate < RATE_RESET:
            warning_active = False

        if warning_active:
            warning.config(
                text="SLOW DOWN",
                fg="#ff4444"
            )

        elif speaking:
            warning.config(
                text="Good pace",
                fg="#66ff99"
            )

        else:
            warning.config(
                text="Listening...",
                fg="white"
            )

        rate_label.config(
            text=f"Estimated rate: {rate:.2f} syllables/s"
        )

    except Exception as e:
        status_label.config(text=f"Error: {e}")

    root.after(UPDATE_MS, update)


# =====================================================
# 5. START / STOP
# =====================================================

def close():

    recorder.stop()
    root.destroy()


root.protocol("WM_DELETE_WINDOW", close)

print("Live speech monitoring started")

root.after(UPDATE_MS, update)

try:
    root.mainloop()
finally:
    recorder.stop()
