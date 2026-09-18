#!/usr/bin/env python3
"""
Actor Voice Separation & Dataset Sorter for Applio / RVC
=========================================================
Analyzes dialogue slices from movie/audio datasets, separates multiple voice actors
using acoustic biometric features (MFCC timbre, spectral contrast, pitch F0),
and organizes them into dedicated, pure datasets for Applio.
"""

import os
import sys
import argparse
import shutil
import glob
import json
from dataclasses import dataclass
from pathlib import Path

# Force UTF-8 output on Windows
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
import soundfile as sf
import librosa
from sklearn.cluster import KMeans
from sklearn.preprocessing import StandardScaler


def extract_acoustic_fingerprint(y: np.ndarray, sr: int) -> tuple[np.ndarray | None, float]:
    """
    Extracts a 48-dimensional voice fingerprint (MFCCs, spectral contrast, pitch)
    and median fundamental frequency (F0).
    """
    if len(y) < int(sr * 0.4):
        return None, 0.0

    # Downsample to 16kHz for uniform acoustic feature space
    y_16k = librosa.resample(y, orig_sr=sr, target_sr=16000) if sr != 16000 else y

    # 1. MFCC timbre
    mfcc = librosa.feature.mfcc(y=y_16k, sr=16000, n_mfcc=20)
    mfcc_mean = np.mean(mfcc, axis=1)
    mfcc_std = np.std(mfcc, axis=1)

    # 2. Spectral Contrast (formants)
    contrast = np.mean(librosa.feature.spectral_contrast(y=y_16k, sr=16000), axis=1)

    # 3. Pitch / F0 via YIN
    try:
        f0 = librosa.yin(y_16k, fmin=55, fmax=450, sr=16000)
        f0_valid = f0[np.isfinite(f0) & (f0 > 55)]
        f0_med = float(np.median(f0_valid)) if len(f0_valid) > 0 else 180.0
    except Exception:
        f0_med = 180.0

    feat = np.concatenate([mfcc_mean, mfcc_std, contrast, [f0_med]])
    return feat, f0_med


def classify_voice_type(pitch: float) -> str:
    """Classifies human voice type based on median pitch (Hz)."""
    if pitch > 260.0:
        return f"Child / High Female ({pitch:.0f} Hz)"
    elif pitch > 175.0:
        return f"Adult Female / Neutral ({pitch:.0f} Hz)"
    elif pitch > 130.0:
        return f"Adult Male / Tenor ({pitch:.0f} Hz)"
    else:
        return f"Deep Male / Baritone ({pitch:.0f} Hz)"


@dataclass
class ActorFolderInfo:
    name: str
    path: str
    voice_type: str
    slices: int
    minutes: float
    samples: list[str]


def separate_dataset(
    dataset_dir: str,
    output_base_dir: str = r"C:\Applio-3.6.4\assets\datasets",
    num_actors: int = 2,
    actor_names: str = "",
    reference_file: str = "",
    references: dict[str, str] | None = None,
    similarity_thresh: float = 0.82,
    copy_mode: bool = True,
):
    dataset_path = Path(dataset_dir)
    if not dataset_path.exists():
        print(f"[Error] Dataset directory '{dataset_dir}' does not exist!", file=sys.stderr)
        return

    wav_files = sorted(list(dataset_path.glob("*.wav")))
    if not wav_files:
        print(f"[Error] No .wav files found in '{dataset_dir}'!", file=sys.stderr)
        return

    ds_name = dataset_path.name
    print("=" * 65)
    print("  Actor Voice Separation & Dataset Sorter for Applio")
    print(f"  Source Dataset: {ds_name} ({len(wav_files)} slices)")
    print(f"  Target Output:  {output_base_dir}")
    print("=" * 65)

    # Step 1: Extract features for all slices
    print(f"\n[1/3] Analyzing vocal characteristics across {len(wav_files)} slices...")
    features = []
    pitches = []
    durations = []
    valid_wavs = []

    for f in wav_files:
        try:
            y, sr = sf.read(str(f), dtype="float32")
            dur = len(y) / sr
            feat, f0 = extract_acoustic_fingerprint(y, sr)
            if feat is not None:
                features.append(feat)
                pitches.append(f0)
                durations.append(dur)
                valid_wavs.append(f)
        except Exception as ex:
            print(f"  [Warning] Skipping {f.name}: {ex}")

    if not valid_wavs:
        print("[Error] No valid audio slices could be processed!", file=sys.stderr)
        return

    print(f"  --> Analyzed {len(valid_wavs)} slices. Total clean speech: {sum(durations)/60:.1f} minutes.")
    print(f"  --> Pitch Range: {min(pitches):.0f} Hz - {max(pitches):.0f} Hz (Median: {np.median(pitches):.0f} Hz)")

    # Step 2: Multi-Actor Reference Matching Mode
    if references:
        print(f"\n[2/3] Matching slices against {len(references)} target actor references...")
        ref_feats = {}
        for act_name, ref_p in references.items():
            resolved_p = Path(ref_p) if os.path.isabs(ref_p) else dataset_path / ref_p
            if not resolved_p.exists():
                print(f"  [Error] Reference file not found: {resolved_p}", file=sys.stderr)
                return
            ref_y, ref_sr = sf.read(str(resolved_p), dtype="float32")
            rf, rf0 = extract_acoustic_fingerprint(ref_y, ref_sr)
            if rf is None:
                print(f"  [Error] Could not extract fingerprint from: {resolved_p}", file=sys.stderr)
                return
            ref_feats[act_name] = (rf / (np.linalg.norm(rf) + 1e-8), rf0, resolved_p.name)
            print(f"  Loaded prototype [{act_name}]: {resolved_p.name} (Pitch: {rf0:.1f} Hz)")

        actor_results = {act: {"slices": [], "pitches": [], "dur": 0.0} for act in references}
        for w, feat, f0, dur in zip(valid_wavs, features, pitches, durations):
            w_norm = feat / (np.linalg.norm(feat) + 1e-8)
            sims = {act: float(np.dot(w_norm, ref_feats[act][0])) for act in references}
            best_act = max(sims.keys(), key=lambda k: sims[k])
            actor_results[best_act]["slices"].append(w)
            actor_results[best_act]["pitches"].append(f0)
            actor_results[best_act]["dur"] += dur

        print("\n[3/3] Exporting separated actor datasets to Applio...")
        print("\n" + "=" * 65)
        print("ACTOR VOICE SEPARATION COMPLETED SUCCESSFULLY!")
        print(f"Source: {ds_name} ({len(valid_wavs)} slices separated into {len(references)} actors)\n")

        for act_name, data in actor_results.items():
            target_name = f"{ds_name}_{act_name}"
            target_dir = Path(output_base_dir) / target_name
            target_dir.mkdir(parents=True, exist_ok=True)

            for w in data["slices"]:
                shutil.copy2(w, target_dir / w.name)

            avg_p = float(np.mean(data["pitches"])) if data["pitches"] else 0.0
            vtype = classify_voice_type(avg_p)
            samples = [w.name for w in data["slices"][:3]]

            print(f"Actor Dataset: {target_name}")
            print(f"  Voice Profile: {vtype}")
            print(f"  Clean Speech:  {len(data['slices'])} slices ({data['dur']/60:.2f} minutes)")
            print(f"  Applio Path:   {target_dir}")
            print(f"  Sample Files:  {', '.join(samples)}")
            print()

        print("--> In Applio GUI: Go to Train tab, choose any actor dataset above, and train!")
        print("=" * 65)
        return

    # Step 2b: Single Reference Voice Matching Mode
    if reference_file and os.path.exists(reference_file):
        print(f"\n[2/3] Matching against reference voice: {reference_file}...")
        ref_y, ref_sr = sf.read(reference_file, dtype="float32")
        ref_feat, ref_f0 = extract_acoustic_fingerprint(ref_y, ref_sr)
        if ref_feat is None:
            print("[Error] Could not extract features from reference file!", file=sys.stderr)
            return

        ref_norm = ref_feat / (np.linalg.norm(ref_feat) + 1e-8)
        norm_feats = np.array([f / (np.linalg.norm(f) + 1e-8) for f in features])
        similarities = np.dot(norm_feats, ref_norm)

        matched_name = f"{ds_name}_target_actor"
        other_name = f"{ds_name}_other_actors"
        matched_dir = Path(output_base_dir) / matched_name
        other_dir = Path(output_base_dir) / other_name
        matched_dir.mkdir(parents=True, exist_ok=True)
        other_dir.mkdir(parents=True, exist_ok=True)

        matched_cnt = 0
        matched_dur = 0.0
        other_cnt = 0
        for w, sim, dur in zip(valid_wavs, similarities, durations):
            if sim >= similarity_thresh:
                shutil.copy2(w, matched_dir / w.name)
                matched_cnt += 1
                matched_dur += dur
            else:
                shutil.copy2(w, other_dir / w.name)
                other_cnt += 1

        print("\n" + "=" * 65)
        print("ACTOR SEPARATION COMPLETED!")
        print(f"  Target Actor ({matched_name}): {matched_cnt} slices ({matched_dur/60:.2f} mins)")
        print(f"  Other Actors ({other_name}): {other_cnt} slices ({(sum(durations)-matched_dur)/60:.2f} mins)")
        print(f"  Target Dataset Ready in Applio: {matched_dir}")
        print("=" * 65)
        return

    # Step 3: Unsupervised Multi-Actor Clustering
    k = max(2, min(num_actors, len(valid_wavs)))
    print(f"\n[2/3] Clustering into {k} distinct voice actors...")

    X = StandardScaler().fit_transform(np.array(features))
    kmeans = KMeans(n_clusters=k, random_state=42, n_init=10)
    labels = kmeans.fit_predict(X)

    # Determine custom or auto names
    user_names = [n.strip() for n in actor_names.split(",") if n.strip()] if actor_names else []

    # Sort clusters by pitch (highest pitch first, e.g. Child -> Female -> Male)
    cluster_pitch_order = sorted(range(k), key=lambda c: np.mean([pitches[i] for i in range(len(pitches)) if labels[i] == c]), reverse=True)

    print("\n[3/3] Exporting separated actor datasets to Applio...")
    actor_folders: list[ActorFolderInfo] = []

    for rank, cl_idx in enumerate(cluster_pitch_order):
        idx_slices = [valid_wavs[i] for i in range(len(valid_wavs)) if labels[i] == cl_idx]
        idx_pitches = [pitches[i] for i in range(len(valid_wavs)) if labels[i] == cl_idx]
        idx_dur = sum(durations[i] for i in range(len(valid_wavs)) if labels[i] == cl_idx)
        avg_f0 = float(np.mean(idx_pitches))
        vtype = classify_voice_type(avg_f0)

        if rank < len(user_names):
            cname = user_names[rank]
        else:
            # Auto name based on voice type
            tag = "child_or_high" if avg_f0 > 260 else ("female_or_lead" if avg_f0 > 175 else "male_or_deep")
            cname = f"actor{rank + 1}_{tag}"

        target_folder_name = f"{ds_name}_{cname}"
        target_dir = Path(output_base_dir) / target_folder_name
        target_dir.mkdir(parents=True, exist_ok=True)

        for w in idx_slices:
            if copy_mode:
                shutil.copy2(w, target_dir / w.name)
            else:
                shutil.move(w, target_dir / w.name)

        actor_folders.append(ActorFolderInfo(
            name=target_folder_name,
            path=str(target_dir),
            voice_type=vtype,
            slices=len(idx_slices),
            minutes=round(idx_dur / 60.0, 2),
            samples=[w.name for w in idx_slices[:3]],
        ))

    print("\n" + "=" * 65)
    print("ACTOR VOICE SEPARATION COMPLETED SUCCESSFULLY!")
    print(f"Source: {ds_name} ({len(valid_wavs)} slices separated into {k} actors)\n")

    for info in actor_folders:
        print(f"Actor Dataset: {info.name}")
        print(f"  Voice Profile: {info.voice_type}")
        print(f"  Clean Speech:  {info.slices} slices ({info.minutes} minutes)")
        print(f"  Applio Path:   {info.path}")
        print(f"  Sample Files:  {', '.join(info.samples)}")
        print()

    print("--> In Applio GUI: Go to Train tab, choose any of the actor datasets above, and train!")
    print("=" * 65)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Separate multiple voice actors from movie dataset into individual Applio datasets")
    parser.add_argument("--dataset-dir", "-d", required=True, help="Path to input dataset folder with .wav slices")
    parser.add_argument("--output-base-dir", "-o", default=r"C:\Applio-3.6.4\assets\datasets", help="Applio assets dataset root folder")
    parser.add_argument("--num-actors", "-k", type=int, default=2, help="Number of actors to cluster into (e.g. 2 or 3)")
    parser.add_argument("--names", "-n", default="", help="Comma-separated custom names (e.g. 'child,mother' or 'actor1,actor2')")
    parser.add_argument("--reference-file", "-ref", default="", help="Path to reference .wav slice of a single actor")
    parser.add_argument("--references", "-refs", default="", help="Comma-separated mapping of actor names to reference files (e.g. 'child:000.wav,female:001.wav,male:002.wav')")
    parser.add_argument("--thresh", type=float, default=0.82, help="Cosine similarity threshold for reference matching (default: 0.82)")

    args = parser.parse_args()

    refs_map = None
    if args.references:
        refs_map = {}
        for part in args.references.split(","):
            part = part.strip()
            if ":" in part:
                k, v = part.split(":", 1)
                refs_map[k.strip()] = v.strip()
            elif "=" in part:
                k, v = part.split("=", 1)
                refs_map[k.strip()] = v.strip()

    separate_dataset(
        dataset_dir=args.dataset_dir,
        output_base_dir=args.output_base_dir,
        num_actors=args.num_actors,
        actor_names=args.names,
        reference_file=args.reference_file,
        references=refs_map,
        similarity_thresh=args.thresh,
    )
