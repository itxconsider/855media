#!/usr/bin/env python3
"""
Dataset Cleaning & Vocal Mastering Tool for Applio / RVC
=========================================================
Extracts pure speech vocals, eliminates background music, podcast jingles, and ambient noise
using AI Hybrid Demucs on RTX 5070 Ti, then segments and normalizes into optimal
training audio slices for Applio.
"""

import os
import sys
import argparse
import subprocess
import tempfile
import json
import time
from pathlib import Path

# Force UTF-8 output on Windows
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

import numpy as np
import torch
import torchaudio
from torchaudio.pipelines import HDEMUCS_HIGH_MUSDB_PLUS
import soundfile as sf
from scipy import signal


def decode_to_wav(ffmpeg_path: str, input_file: str, output_wav: str) -> bool:
    """Decode any audio/video container to 44.1kHz 16-bit stereo WAV."""
    cmd = [
        ffmpeg_path,
        "-y",
        "-i", input_file,
        "-vn",
        "-ac", "2",
        "-ar", "44100",
        "-c:a", "pcm_s16le",
        output_wav
    ]
    res = subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return res.returncode == 0 and os.path.exists(output_wav) and os.path.getsize(output_wav) > 1024


def load_demucs_model(device: torch.device):
    """Load pretrained HDEMUCS model onto the target device."""
    print(f"[Demucs] Loading Hybrid Demucs model onto {device}...")
    bundle = HDEMUCS_HIGH_MUSDB_PLUS
    model = bundle.get_model().to(device)
    model.eval()
    return model, bundle.sample_rate


def separate_vocals_tensor(model, audio_tensor: torch.Tensor, device: torch.device, chunk_sec: float = 60.0, overlap_sec: float = 2.0, sr: int = 44100) -> torch.Tensor:
    """
    Separates vocals from a stereo audio tensor (2, N) using chunking to conserve VRAM.
    Returns vocal tensor (2, N).
    """
    total_samples = audio_tensor.shape[-1]
    chunk_samples = int(chunk_sec * sr)
    overlap_samples = int(overlap_sec * sr)
    step_samples = chunk_samples - overlap_samples

    vocals_out = torch.zeros_like(audio_tensor)
    weight_out = torch.zeros(1, total_samples)

    start = 0
    with torch.no_grad():
        while start < total_samples:
            end = min(start + chunk_samples, total_samples)
            chunk = audio_tensor[:, start:end].unsqueeze(0).to(device) # (1, 2, L)

            # Demucs inference: output is (batch, sources, channels, time)
            # Sources: 0=drums, 1=bass, 2=other, 3=vocals
            sources = model(chunk)
            vocals_chunk = sources[0, 3].cpu() # (2, L)

            # Window ramp for smooth crossfade
            cur_len = end - start
            window = torch.ones(1, cur_len)
            if start > 0 and cur_len > overlap_samples:
                ramp_up = torch.linspace(0, 1, overlap_samples)
                window[0, :overlap_samples] = ramp_up
            if end < total_samples and cur_len > overlap_samples:
                ramp_down = torch.linspace(1, 0, overlap_samples)
                window[0, -overlap_samples:] = ramp_down

            vocals_out[:, start:end] += vocals_chunk * window
            weight_out[:, start:end] += window

            if end >= total_samples:
                break
            start += step_samples

    # Normalize overlapping weights
    weight_out[weight_out == 0] = 1.0
    vocals_out = vocals_out / weight_out
    return vocals_out


def apply_highpass(audio_mono: np.ndarray, sr: int, cutoff: float = 55.0) -> np.ndarray:
    """Apply high-pass Butterworth filter to eliminate AC hum and mic rumble."""
    b, a = signal.butter(5, cutoff, btype="high", fs=sr)
    return signal.filtfilt(b, a, audio_mono)


def detect_speech_slices(
    audio_mono: np.ndarray,
    sr: int,
    min_slice_sec: float = 2.5,
    max_slice_sec: float = 10.0,
    silence_thresh_db: float = -40.0,
    min_silence_sec: float = 0.35,
) -> list[tuple[int, int]]:
    """
    Energy-based Voice Activity Detection and chunk segmentation.
    Slices continuous speech at natural silence breaks.
    """
    frame_ms = 40
    hop_ms = 20
    frame_len = int(sr * frame_ms / 1000)
    hop_len = int(sr * hop_ms / 1000)

    # Frame energy (RMS in dBFS)
    num_frames = (len(audio_mono) - frame_len) // hop_len + 1
    if num_frames <= 0:
        return []

    # Fast frame RMS calculation
    frames = np.lib.stride_tricks.sliding_window_view(audio_mono[:(num_frames - 1) * hop_len + frame_len], frame_len)[::hop_len]
    rms = np.sqrt(np.mean(frames ** 2, axis=-1) + 1e-12)
    db = 20 * np.log10(rms)

    is_speech = db > silence_thresh_db

    # Find continuous speech intervals
    min_silence_frames = int((min_silence_sec * 1000) / hop_ms)
    min_slice_samples = int(min_slice_sec * sr)
    max_slice_samples = int(max_slice_sec * sr)

    slices = []
    in_speech = False
    speech_start_sample = 0
    silence_count = 0

    for i, active in enumerate(is_speech):
        sample_pos = i * hop_len
        if active:
            if not in_speech:
                in_speech = True
                speech_start_sample = max(0, sample_pos - int(0.15 * sr)) # Pre-pad 150ms
            silence_count = 0

            # If current segment exceeds max_slice_sec, force a slice at current position
            if (sample_pos - speech_start_sample) >= max_slice_samples:
                slice_end = min(len(audio_mono), sample_pos + int(0.1 * sr))
                if (slice_end - speech_start_sample) >= min_slice_samples:
                    slices.append((speech_start_sample, slice_end))
                speech_start_sample = sample_pos
        else:
            if in_speech:
                silence_count += 1
                if silence_count >= min_silence_frames:
                    in_speech = False
                    speech_end_sample = min(len(audio_mono), sample_pos + int(0.15 * sr)) # Post-pad 150ms
                    duration = speech_end_sample - speech_start_sample
                    if duration >= min_slice_samples:
                        # If duration is longer than max, split into chunks
                        cur = speech_start_sample
                        while cur < speech_end_sample:
                            nxt = min(cur + max_slice_samples, speech_end_sample)
                            if (nxt - cur) >= min_slice_samples:
                                slices.append((cur, nxt))
                            elif len(slices) > 0:
                                # Merge small leftover with previous slice if within limits
                                prev_s, prev_e = slices[-1]
                                if (nxt - prev_s) <= max_slice_samples * 1.2:
                                    slices[-1] = (prev_s, nxt)
                            cur = nxt

    return slices


def process_dataset(
    input_dir: str,
    output_dir: str,
    applio_dataset_dir: str = "",
    target_sr: int = 40000,
    max_duration_min: float = 25.0,
    ffmpeg_path: str = r"C:\Applio-3.6.4\ffmpeg.exe",
    device_name: str = "cuda:0",
):
    """
    Main processing pipeline:
    Iterates through episodes, removes music via Demucs, cuts into clean vocal slices,
    normalizes, and outputs Applio-ready dataset.
    """
    device = torch.device(device_name if torch.cuda.is_available() else "cpu")
    print("=" * 60)
    print(f"Applio Audio Dataset Cleaner & Vocal Master")
    print(f"Device: {device} ({torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'CPU'})")
    print(f"Input: {input_dir}")
    print(f"Output: {output_dir}")
    print(f"Target Sample Rate: {target_sr} Hz (Mono 16-bit PCM)")
    print(f"Target Clean Speech Duration: {max_duration_min:.1f} minutes")
    print("=" * 60)

    os.makedirs(output_dir, exist_ok=True)
    if applio_dataset_dir:
        os.makedirs(applio_dataset_dir, exist_ok=True)

    # 1. Collect files
    raw_files = [
        os.path.join(input_dir, f)
        for f in os.listdir(input_dir)
        if f.lower().endswith((".mp3", ".webm", ".m4a", ".wav", ".aac", ".ogg"))
        and not f.endswith(".mp4")
    ]
    raw_files.sort()
    print(f"Discovered {len(raw_files)} audio episodes in input directory.")

    if not raw_files:
        print("No audio files found! Exiting.")
        return

    # 2. Load Demucs model
    model, demucs_sr = load_demucs_model(device)
    resampler_to_target = torchaudio.transforms.Resample(orig_freq=demucs_sr, new_freq=target_sr)

    total_clean_seconds = 0.0
    total_slices = 0
    file_count = 0
    max_target_seconds = max_duration_min * 60.0 if max_duration_min > 0 else float("inf")

    with tempfile.TemporaryDirectory() as tmp_dir:
        for idx, file_path in enumerate(raw_files):
            if total_clean_seconds >= max_target_seconds:
                print(f"\n[Target Reached] Reached desired dataset duration of {total_clean_seconds/60:.1f} minutes.")
                break

            fname = Path(file_path).name
            print(f"\n[{idx + 1}/{len(raw_files)}] Processing: {fname[:45]}...")
            
            # Step A: Decode to standard WAV via FFmpeg
            tmp_wav = os.path.join(tmp_dir, f"input_{idx:03d}.wav")
            if not decode_to_wav(ffmpeg_path, file_path, tmp_wav):
                print(f"  [Error] Failed to decode {fname} with FFmpeg. Skipping.")
                continue

            # Step B: Load audio tensor via soundfile
            data, file_sr = sf.read(tmp_wav, dtype="float32")
            if data.ndim == 1:
                data = np.stack([data, data], axis=0)
            else:
                data = data.T
            audio_tensor = torch.from_numpy(data)
            if file_sr != demucs_sr:
                audio_tensor = torchaudio.transforms.Resample(file_sr, demucs_sr)(audio_tensor)

            orig_dur = audio_tensor.shape[-1] / demucs_sr
            print(f"  Audio duration: {orig_dur:.1f}s ({orig_dur/60:.1f} min)")

            # Step C: Separate vocals via Demucs
            t0 = time.time()
            vocals_tensor = separate_vocals_tensor(model, audio_tensor, device, chunk_sec=60.0, overlap_sec=2.0, sr=demucs_sr)
            elapsed = time.time() - t0
            print(f"  Vocal extraction completed in {elapsed:.1f}s (Speed: {orig_dur/max(0.1, elapsed):.1f}x real-time)")

            # Step D: Convert to mono and filter
            vocals_mono = vocals_tensor.mean(dim=0).numpy() # (N,)
            vocals_filtered = apply_highpass(vocals_mono, demucs_sr, cutoff=55.0)

            # Step E: Detect speech slices (ignoring jingles, music, and pauses)
            slices = detect_speech_slices(
                vocals_filtered,
                demucs_sr,
                min_slice_sec=2.5,
                max_slice_sec=10.0,
                silence_thresh_db=-40.0,
                min_silence_sec=0.35,
            )
            print(f"  Detected {len(slices)} clean speech slices.")

            # Step F: Export slices
            saved_in_file = 0
            for s_idx, (start_samp, end_samp) in enumerate(slices):
                slice_dur = (end_samp - start_samp) / demucs_sr
                slice_audio = vocals_filtered[start_samp:end_samp]

                # Peak normalize to -1.0 dB (0.891)
                peak = np.max(np.abs(slice_audio))
                if peak > 1e-4:
                    slice_audio = (slice_audio / peak) * 0.891
                else:
                    continue # Pure silence, skip

                # Apply 20ms fade in/out
                fade_len = int(0.02 * demucs_sr)
                if len(slice_audio) > 2 * fade_len:
                    fade_in = np.sin(np.linspace(0, np.pi / 2, fade_len)) ** 2
                    fade_out = np.sin(np.linspace(np.pi / 2, 0, fade_len)) ** 2
                    slice_audio[:fade_len] *= fade_in
                    slice_audio[-fade_len:] *= fade_out

                # Resample to target SR
                slice_torch = torch.from_numpy(slice_audio).unsqueeze(0).float()
                slice_resampled = resampler_to_target(slice_torch).squeeze(0).numpy()

                # Output filename
                out_name = f"seng_dyna_vocal_{idx:03d}_{s_idx:03d}.wav"
                out_path = os.path.join(output_dir, out_name)

                # Save 16-bit PCM WAV
                sf.write(out_path, slice_resampled, target_sr, subtype="PCM_16")

                # Copy to Applio dataset dir if specified
                if applio_dataset_dir:
                    applio_path = os.path.join(applio_dataset_dir, out_name)
                    sf.write(applio_path, slice_resampled, target_sr, subtype="PCM_16")

                total_clean_seconds += slice_dur
                total_slices += 1
                saved_in_file += 1

                if total_clean_seconds >= max_target_seconds:
                    break

            file_count += 1
            print(f"  Saved {saved_in_file} slices from this episode. Total clean speech: {total_clean_seconds/60:.1f} mins.")

            # Clean up temp files
            if os.path.exists(tmp_wav):
                os.remove(tmp_wav)

    # 3. Summary Report
    summary = {
        "status": "success",
        "episodes_processed": file_count,
        "total_slices": total_slices,
        "total_clean_seconds": round(total_clean_seconds, 2),
        "total_clean_minutes": round(total_clean_seconds / 60.0, 2),
        "sample_rate": target_sr,
        "format": "WAV PCM_16 Mono",
        "output_directory": output_dir,
        "applio_dataset_directory": applio_dataset_dir,
    }

    summary_file = os.path.join(output_dir, "dataset_summary.json")
    with open(summary_file, "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

    print("\n" + "=" * 60)
    print("DATASET PREPARATION COMPLETED SUCCESSFULLY!")
    print(f"  Total Clean Speech: {total_clean_seconds/60:.2f} minutes ({total_clean_seconds:.1f} seconds)")
    print(f"  Total Slices: {total_slices} high-quality chunks")
    print(f"  Average Slice Duration: {total_clean_seconds/max(1, total_slices):.2f} seconds")
    print(f"  Clean Dataset Location: {output_dir}")
    if applio_dataset_dir:
        print(f"  Applio Ready Location:  {applio_dataset_dir}")
    print("=" * 60)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Clean and slice audio dataset for Applio / RVC")
    parser.add_argument("--input-dir", "-i", default=r"d:\repos\855Media\downloads\sengdynadataset", help="Path to raw audio folder")
    parser.add_argument("--output-dir", "-o", default=r"d:\repos\855Media\downloads\sengdynadataset_applio_clean", help="Path to cleaned dataset folder")
    parser.add_argument("--applio-dir", "-a", default=r"C:\Applio-3.6.4\assets\datasets\seng_dyna_apollo", help="Applio assets dataset folder")
    parser.add_argument("--sample-rate", "-sr", type=int, default=40000, help="Target sample rate (40000 or 48000)")
    parser.add_argument("--target-duration-minutes", "-d", type=float, default=25.0, help="Target clean speech duration in minutes (default 25.0 min)")
    parser.add_argument("--ffmpeg-path", default=r"C:\Applio-3.6.4\ffmpeg.exe", help="Path to FFmpeg executable")
    parser.add_argument("--device", default="cuda:0", help="Inference device (cuda:0 or cpu)")

    args = parser.parse_args()
    process_dataset(
        input_dir=args.input_dir,
        output_dir=args.output_dir,
        applio_dataset_dir=args.applio_dir,
        target_sr=args.sample_rate,
        max_duration_min=args.target_duration_minutes,
        ffmpeg_path=args.ffmpeg_path,
        device_name=args.device,
    )
