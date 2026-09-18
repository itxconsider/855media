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
from dataclasses import dataclass
from pathlib import Path

# Force UTF-8 output on Windows with safe fallback
if sys.platform == "win32":
    try:
        reconfigure = getattr(sys.stdout, "reconfigure", None)
        if callable(reconfigure):
            reconfigure(encoding="utf-8", errors="replace")
        reconfigure_err = getattr(sys.stderr, "reconfigure", None)
        if callable(reconfigure_err):
            reconfigure_err(encoding="utf-8", errors="replace")
    except Exception:
        pass

import numpy as np
import torch
import torchaudio
from torchaudio.pipelines import HDEMUCS_HIGH_MUSDB_PLUS
import soundfile as sf
from scipy import signal  # type: ignore


def find_ffmpeg(custom_path: str = "") -> str:
    """Find FFmpeg executable from Applio, 855Media, or system PATH."""
    candidates = [
        custom_path,
        r"C:\Applio-3.6.4\ffmpeg.exe",
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "855Media", "bin", "Debug", "net10.0", "ffmpeg.exe"),
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "ffmpeg.exe"),
    ]
    for c in candidates:
        if c and os.path.exists(c):
            return os.path.abspath(c)
    import shutil
    which_ffmpeg = shutil.which("ffmpeg")
    if which_ffmpeg:
        return which_ffmpeg
    return "ffmpeg"


def decode_to_wav(ffmpeg_path: str, input_file: str, output_wav: str) -> bool:
    """Decode any movie (mp4, mkv, mov, etc.) or audio container to 44.1kHz 16-bit stereo WAV."""
    actual_ffmpeg = find_ffmpeg(ffmpeg_path)
    cmd = [
        actual_ffmpeg,
        "-y",
        "-i", input_file,
        "-vn",
        "-ac", "2",
        "-ar", "44100",
        "-c:a", "pcm_s16le",
        output_wav
    ]
    res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if res.returncode != 0 or not os.path.exists(output_wav) or os.path.getsize(output_wav) <= 1024:
        if res.stderr:
            err_msg = res.stderr.decode("utf-8", errors="ignore")[-300:]
            print(f"  [FFmpeg Error]: {err_msg}", file=sys.stderr)
        return False
    return True


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


def extract_speaker_embedding(audio_mono: np.ndarray, sr: int) -> np.ndarray | None:
    """
    Extracts an acoustic speaker biometric vector (MFCC timbre, spectral contrast, pitch F0).
    Enables clustering of dialogue into distinct actors or matching against a target reference voice.
    """
    try:
        import librosa
    except ImportError:
        return None

    if len(audio_mono) < int(sr * 0.4):
        return None

    # Resample to 16kHz for normalized acoustic feature representation
    if sr != 16000:
        y_16k = librosa.resample(audio_mono, orig_sr=sr, target_sr=16000)
    else:
        y_16k = audio_mono

    # 1. Mel-Frequency Cepstral Coefficients (vocal tract resonance / timbre)
    mfcc = librosa.feature.mfcc(y=y_16k, sr=16000, n_mfcc=20)
    mfcc_mean = np.mean(mfcc, axis=1)
    mfcc_std = np.std(mfcc, axis=1)

    # 2. Spectral Contrast (formants & harmonics energy)
    contrast = np.mean(librosa.feature.spectral_contrast(y=y_16k, sr=16000), axis=1)

    # 3. Pitch / Fundamental Frequency F0 (identifies male/female/baritone/soprano)
    try:
        f0 = librosa.yin(y_16k, fmin=55, fmax=420, sr=16000)
        f0_valid = f0[np.isfinite(f0) & (f0 > 55)]
        f0_med = np.median(f0_valid) if len(f0_valid) > 0 else 120.0
        f0_std = np.std(f0_valid) if len(f0_valid) > 0 else 20.0
    except Exception:
        f0_med, f0_std = 120.0, 20.0

    feat = np.concatenate([mfcc_mean, mfcc_std, contrast, [f0_med / 100.0, f0_std / 100.0]])
    norm = np.linalg.norm(feat)
    if norm > 1e-6:
        feat = feat / norm
    return feat


VALID_EXTENSIONS = (
    ".mp4", ".mkv", ".mov", ".avi", ".webm", ".flv", ".ts", ".m4v",
    ".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac", ".opus"
)


@dataclass
class SliceItem:
    audio: np.ndarray
    dur: float
    emb: np.ndarray
    name: str


def sanitize_name(name: str) -> str:
    """Sanitize string to an ASCII-clean folder/dataset name for Applio / RVC compatibility."""
    clean = "".join(c if (c.isascii() and c.isalnum()) or c in ("_", "-") else "_" for c in name)
    clean = "_".join(part for part in clean.split("_") if part)
    clean = clean.strip("-_").lower()
    return clean or "movie_voice"


def process_dataset(
    input_path: str,
    output_dir: str = "",
    applio_dataset_dir: str = "",
    dataset_name: str = "",
    target_sr: int = 40000,
    max_duration_min: float = 25.0,
    ffmpeg_path: str = "",
    device_name: str = "cuda:0",
    target_voice: str = "",
    similarity_thresh: float = 0.82,
    num_speakers: int = 1,
):
    """
    Main processing pipeline:
    Extracts pure speech vocals from movie video/audio, strips BGM/foley with Demucs,
    slices into optimal training chunks (2.5s - 10s), normalizes, and outputs
    directly into Applio's training datasets folder.

    Supports:
      - target_voice: matches dialogue slices to a specific actor reference clip
      - num_speakers: automatically clusters dialogues into N distinct actor datasets
    """
    device = torch.device(device_name if torch.cuda.is_available() else "cpu")
    actual_ffmpeg = find_ffmpeg(ffmpeg_path)

    # 1. Resolve files and dataset name
    input_p = Path(input_path)
    if not input_p.exists():
        print(f"[Error] Input path '{input_path}' does not exist!", file=sys.stderr)
        return

    if input_p.is_file():
        raw_files = [str(input_p.resolve())]
        inferred_name = input_p.stem
    else:
        raw_files = [
            str(p.resolve())
            for p in input_p.iterdir()
            if p.is_file() and p.suffix.lower() in VALID_EXTENSIONS
        ]
        raw_files.sort()
        inferred_name = input_p.name

    if not raw_files:
        print(f"[Error] No supported video/audio files found in '{input_path}'!", file=sys.stderr)
        return

    ds_name = sanitize_name(dataset_name or inferred_name)
    if not output_dir:
        output_dir = os.path.join(r"d:\repos\855Media\downloads", f"{ds_name}_clean")
    if not applio_dataset_dir:
        applio_dataset_dir = os.path.join(r"C:\Applio-3.6.4\assets\datasets", ds_name)

    # 2. Handle Target Voice Reference if provided
    ref_embedding = None
    if target_voice and os.path.exists(target_voice):
        print(f"[Speaker Diarization] Loading reference voice from: {target_voice}")
        ref_data, ref_sr = sf.read(target_voice, dtype="float32")
        if ref_data.ndim > 1:
            ref_data = ref_data.mean(axis=-1)
        ref_embedding = extract_speaker_embedding(ref_data, ref_sr)
        if ref_embedding is not None:
            print(f"  --> Reference voice profile extracted. Target similarity threshold: {similarity_thresh}")
        else:
            print(f"  [Warning] Could not extract acoustic profile from reference voice. Proceeding without matching.")

    print("=" * 65)
    print(f"Movie Audio Cleaner & Applio Dataset Preparer")
    print(f"Device: {device} ({torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'CPU'})")
    print(f"Dataset Name: {ds_name}")
    print(f"Input ({len(raw_files)} files): {input_path}")
    print(f"Output Clean Directory: {output_dir}")
    print(f"Applio Dataset Directory: {applio_dataset_dir}")
    print(f"Target Sample Rate: {target_sr} Hz (Mono 16-bit PCM)")
    print(f"Target Duration: {max_duration_min:.1f} min ({'unlimited' if max_duration_min <= 0 else f'up to {max_duration_min} min'})")
    if ref_embedding is not None:
        print(f"Mode: Single Target Actor Filter (Threshold: {similarity_thresh})")
    elif num_speakers > 1:
        print(f"Mode: Multi-Speaker Diarization ({num_speakers} distinct actor clusters)")
    else:
        print(f"Mode: All Clean Dialogue")
    print("=" * 65)

    os.makedirs(output_dir, exist_ok=True)
    if applio_dataset_dir:
        os.makedirs(applio_dataset_dir, exist_ok=True)

    # 3. Load Demucs model
    model, demucs_sr = load_demucs_model(device)
    resampler_to_target = torchaudio.transforms.Resample(orig_freq=demucs_sr, new_freq=target_sr)

    total_clean_seconds = 0.0
    total_slices = 0
    file_count = 0
    max_target_seconds = max_duration_min * 60.0 if max_duration_min > 0 else float("inf")

    # In-memory buffer for multi-speaker clustering
    collected_slices: list[SliceItem] = []

    with tempfile.TemporaryDirectory() as tmp_dir:
        for idx, file_path in enumerate(raw_files):
            if total_clean_seconds >= max_target_seconds:
                print(f"\n[Target Reached] Reached desired dataset duration of {total_clean_seconds/60:.1f} minutes.")
                break

            fname = Path(file_path).name
            print(f"\n[{idx + 1}/{len(raw_files)}] Processing: {fname[:55]}...")

            # Step A: Decode video/audio to standard WAV via FFmpeg
            tmp_wav = os.path.join(tmp_dir, f"input_{idx:03d}.wav")
            if not decode_to_wav(actual_ffmpeg, file_path, tmp_wav):
                print(f"  [Error] Failed to decode '{fname}' with FFmpeg. Skipping.")
                continue

            # Step B: Load audio tensor via soundfile
            try:
                data, file_sr = sf.read(tmp_wav, dtype="float32")
            except Exception as ex:
                print(f"  [Error] Failed to read decoded audio: {ex}. Skipping.")
                continue

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
            vocals_mono = vocals_tensor.mean(dim=0).numpy()  # (N,)
            vocals_filtered = apply_highpass(vocals_mono, demucs_sr, cutoff=55.0)

            # Save master full clean vocal track
            full_clean_path = os.path.join(output_dir, f"{ds_name}_full_clean_vocals.wav")
            try:
                print(f"  Saving master clean vocal track: {Path(full_clean_path).name} ({orig_dur/60:.1f} min)")
                sf.write(full_clean_path, vocals_filtered, demucs_sr, subtype="PCM_16")

                # Also export master MP3 for convenient listening
                mp3_path = os.path.join(output_dir, f"{ds_name}_full_clean_vocals.mp3")
                subprocess.run([
                    actual_ffmpeg, "-y", "-i", full_clean_path,
                    "-b:a", "192k", mp3_path
                ], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                if os.path.exists(mp3_path):
                    print(f"  Exported master clean vocal MP3: {Path(mp3_path).name}")
            except Exception as ex:
                print(f"  [Warning] Could not write master vocal file: {ex}")

            # Step E: Detect speech slices (ignoring jingles, music, explosions, and pauses)
            slices = detect_speech_slices(
                vocals_filtered,
                demucs_sr,
                min_slice_sec=2.5,
                max_slice_sec=10.0,
                silence_thresh_db=-40.0,
                min_silence_sec=0.35,
            )
            print(f"  Detected {len(slices)} clean speech dialogue slices.")

            # Step F: Process slices
            saved_in_file = 0
            rejected_in_file = 0
            for s_idx, (start_samp, end_samp) in enumerate(slices):
                slice_dur = (end_samp - start_samp) / demucs_sr
                slice_audio = vocals_filtered[start_samp:end_samp]

                # Peak normalize to -1.0 dB (0.891)
                peak = np.max(np.abs(slice_audio))
                if peak > 1e-4:
                    slice_audio = (slice_audio / peak) * 0.891
                else:
                    continue  # Pure silence, skip

                # Apply 20ms fade in/out to prevent clicks
                fade_len = int(0.02 * demucs_sr)
                if len(slice_audio) > 2 * fade_len:
                    fade_in = np.sin(np.linspace(0, np.pi / 2, fade_len)) ** 2
                    fade_out = np.sin(np.linspace(np.pi / 2, 0, fade_len)) ** 2
                    slice_audio[:fade_len] *= fade_in
                    slice_audio[-fade_len:] *= fade_out

                # Extract speaker embedding for matching/clustering
                emb = extract_speaker_embedding(slice_audio, demucs_sr)

                # If Target Voice filtering is active:
                if ref_embedding is not None:
                    if emb is None:
                        rejected_in_file += 1
                        continue
                    sim = float(np.dot(ref_embedding, emb))
                    if sim < similarity_thresh:
                        rejected_in_file += 1
                        continue  # Voice belongs to another actor or background character

                # Resample to target SR (40000 or 48000 Hz)
                slice_torch = torch.from_numpy(slice_audio).unsqueeze(0).float()
                slice_resampled = resampler_to_target(slice_torch).squeeze(0).numpy()

                out_name = f"{ds_name}_{idx:03d}_{s_idx:03d}.wav"

                # If multi-speaker clustering is requested, buffer slices for clustering
                if num_speakers > 1:
                    if emb is not None:
                        collected_slices.append(SliceItem(
                            audio=slice_resampled,
                            dur=slice_dur,
                            emb=emb,
                            name=out_name,
                        ))
                        total_clean_seconds += slice_dur
                        total_slices += 1
                        saved_in_file += 1
                else:
                    # Save immediately to output_dir and applio_dataset_dir
                    out_path = os.path.join(output_dir, out_name)
                    sf.write(out_path, slice_resampled, target_sr, subtype="PCM_16")

                    if applio_dataset_dir:
                        applio_path = os.path.join(applio_dataset_dir, out_name)
                        sf.write(applio_path, slice_resampled, target_sr, subtype="PCM_16")

                    total_clean_seconds += slice_dur
                    total_slices += 1
                    saved_in_file += 1

                if total_clean_seconds >= max_target_seconds:
                    break

            file_count += 1
            if ref_embedding is not None:
                print(f"  Target actor matched: {saved_in_file} slices ({rejected_in_file} rejected). Total: {total_clean_seconds/60:.1f} mins.")
            else:
                print(f"  Saved {saved_in_file} slices from this file. Total clean speech: {total_clean_seconds/60:.1f} mins.")

            # Clean up temp file
            if os.path.exists(tmp_wav):
                try:
                    os.remove(tmp_wav)
                except Exception:
                    pass

    # 4. Handle Multi-Speaker Clustering if active
    cluster_results = {}
    if num_speakers > 1 and collected_slices:
        actual_clusters = min(num_speakers, len(collected_slices))
        if actual_clusters < 2:
            print(f"\n[Clustering] Only {len(collected_slices)} slice(s) found. Need at least 2 for clustering. Saving to default dataset.")
            for s in collected_slices:
                out_path = os.path.join(output_dir, s.name)
                sf.write(out_path, s.audio, target_sr, subtype="PCM_16")
                if applio_dataset_dir:
                    applio_path = os.path.join(applio_dataset_dir, s.name)
                    sf.write(applio_path, s.audio, target_sr, subtype="PCM_16")
        else:
            print(f"\n[Clustering] Separating {len(collected_slices)} speech slices into {actual_clusters} distinct actor clusters...")
            from sklearn.cluster import KMeans
            from sklearn.preprocessing import StandardScaler

            X_raw = np.array([item.emb for item in collected_slices])
            X_norm = StandardScaler().fit_transform(X_raw)
            kmeans = KMeans(n_clusters=actual_clusters, random_state=42, n_init=10)
            labels = kmeans.fit_predict(X_norm)

            for spk_idx in range(actual_clusters):
                spk_name = f"{ds_name}_actor{spk_idx + 1}"
                spk_out_dir = os.path.join(output_dir, spk_name)
                spk_applio_dir = os.path.join(r"C:\Applio-3.6.4\assets\datasets", spk_name)
                os.makedirs(spk_out_dir, exist_ok=True)
                os.makedirs(spk_applio_dir, exist_ok=True)

                spk_slices = [s for s, lbl in zip(collected_slices, labels) if lbl == spk_idx]
                spk_dur = sum(s.dur for s in spk_slices)
                for s in spk_slices:
                    sf.write(os.path.join(spk_out_dir, s.name), s.audio, target_sr, subtype="PCM_16")
                    sf.write(os.path.join(spk_applio_dir, s.name), s.audio, target_sr, subtype="PCM_16")

                cluster_results[f"actor_{spk_idx + 1}"] = {
                    "dataset_name": spk_name,
                    "slices": len(spk_slices),
                    "clean_minutes": round(spk_dur / 60.0, 2),
                    "applio_path": spk_applio_dir,
                }
                print(f"  Actor {spk_idx + 1} ({spk_name}): {len(spk_slices)} slices ({spk_dur/60.0:.2f} mins speech)")

    # 5. Summary Report
    summary = {
        "status": "success",
        "dataset_name": ds_name,
        "mode": "target_matching" if ref_embedding is not None else ("multi_speaker_clustering" if num_speakers > 1 else "standard"),
        "files_processed": file_count,
        "total_slices": total_slices,
        "total_clean_seconds": round(total_clean_seconds, 2),
        "total_clean_minutes": round(total_clean_seconds / 60.0, 2),
        "sample_rate": target_sr,
        "format": "WAV PCM_16 Mono",
        "output_directory": output_dir,
        "applio_dataset_directory": applio_dataset_dir,
        "cluster_results": cluster_results,
    }

    summary_file = os.path.join(output_dir, "dataset_summary.json")
    with open(summary_file, "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

    print("\n" + "=" * 65)
    print("DATASET PREPARATION COMPLETED SUCCESSFULLY!")
    print(f"  Dataset Name:           {ds_name}")
    print(f"  Total Clean Speech:     {total_clean_seconds/60:.2f} minutes ({total_clean_seconds:.1f} seconds)")
    print(f"  Total Slices:           {total_slices} dialogue chunks")
    print(f"  Format:                 {target_sr} Hz, 16-bit Mono WAV (Normalized -1.0 dBFS)")
    if num_speakers > 1:
        print(f"  Separated into {num_speakers} actor datasets in Applio:")
        for spk_key, info in cluster_results.items():
            print(f"    - {info['dataset_name']}: {info['slices']} slices ({info['clean_minutes']} min)")
    else:
        print(f"  Applio Ready Location:  {applio_dataset_dir}")
        print(f"  --> In Applio GUI: Select '{ds_name}' under Train -> Dataset Path!")
    print("=" * 65)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Clean movie audio and prepare dataset for Applio / RVC")
    parser.add_argument("--input", "-i", required=True, help="Path to movie file (e.g. movie.mp4, movie.mkv) or folder of files")
    parser.add_argument("--name", "-n", default="", help="Dataset name for Applio (e.g. movie_actor, eva_drama)")
    parser.add_argument("--output-dir", "-o", default="", help="Path to cleaned dataset output folder")
    parser.add_argument("--applio-dir", "-a", default="", help="Applio assets dataset folder (default: C:\\Applio-3.6.4\\assets\\datasets\\<name>)")
    parser.add_argument("--sample-rate", "-sr", type=int, default=40000, help="Target sample rate: 40000 (RVC v2 40k) or 48000 (48k)")
    parser.add_argument("--target-duration-minutes", "-d", type=float, default=25.0, help="Target clean speech duration in minutes (default: 25.0 min, set 0 for unlimited)")
    parser.add_argument("--target-voice", "-tv", default="", help="Path to reference audio sample of target actor to match & filter dialogue")
    parser.add_argument("--similarity-thresh", "-th", type=float, default=0.82, help="Cosine similarity threshold for target voice (default: 0.82)")
    parser.add_argument("--speakers", "-sp", type=int, default=1, help="Number of speakers to separate into distinct actor datasets (e.g. 2 or 3)")
    parser.add_argument("--ffmpeg-path", default="", help="Path to FFmpeg executable (auto-detected if blank)")
    parser.add_argument("--device", default="cuda:0", help="Inference device (cuda:0 or cpu)")

    args = parser.parse_args()
    process_dataset(
        input_path=args.input,
        output_dir=args.output_dir,
        applio_dataset_dir=args.applio_dir,
        dataset_name=args.name,
        target_sr=args.sample_rate,
        max_duration_min=args.target_duration_minutes,
        ffmpeg_path=args.ffmpeg_path,
        device_name=args.device,
        target_voice=args.target_voice,
        similarity_thresh=args.similarity_thresh,
        num_speakers=args.speakers,
    )
