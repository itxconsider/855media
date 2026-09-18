import os
import sys
import argparse
import subprocess
import numpy as np
import soundfile as sf
import torch
from torchaudio.pipelines import HDEMUCS_HIGH_MUSDB_PLUS

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

def load_audio(file_path, target_sr=44100):
    wav_path = file_path
    temp_created = False
    if not file_path.lower().endswith(".wav"):
        wav_path = file_path + ".temp.wav"
        import shutil
        ffmpeg = shutil.which("ffmpeg")
        if not ffmpeg:
            script_dir = os.path.dirname(os.path.abspath(__file__))
            rel_candidates = [
                os.path.join(script_dir, "..", "855Media", "bin", "Debug", "net10.0", "ffmpeg.exe"),
                os.path.join(script_dir, "..", "bin", "ffmpeg.exe"),
                os.path.join(script_dir, "ffmpeg.exe"),
            ]
            for c in rel_candidates:
                if os.path.exists(c):
                    ffmpeg = os.path.abspath(c)
                    break
        if not ffmpeg:
            ffmpeg = "ffmpeg"
        cmd = [ffmpeg, "-y", "-i", file_path, "-vn", "-ac", "2", "-ar", str(target_sr), wav_path]
        subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        temp_created = True
        
    data, sr = sf.read(wav_path, dtype="float32")
    if temp_created and os.path.exists(wav_path):
        try:
            os.remove(wav_path)
        except Exception:
            pass
            
    if data.ndim == 1:
        data = np.stack([data, data], axis=0)
    else:
        # soundfile returns (samples, channels), transpose to (channels, samples)
        data = data.T
        
    tensor = torch.from_numpy(data)
    return tensor, sr

def separate_stems(input_path, output_vocals_path, output_no_vocals_path=None):
    print(f"[Demucs] Loading audio: {input_path}", flush=True)
    waveform, sr = load_audio(input_path, target_sr=44100)
    
    device = torch.device("cuda:0" if torch.cuda.is_available() else "cpu")
    gpu_name = torch.cuda.get_device_name(0) if torch.cuda.is_available() else "CPU"
    print(f"[Demucs] Running on: {device} ({gpu_name})", flush=True)
    
    bundle = HDEMUCS_HIGH_MUSDB_PLUS
    model = bundle.get_model().to(device)
    model.eval()
    
    total_len = waveform.shape[1]
    sources: list[str] = getattr(model, "sources", ["drums", "bass", "other", "vocals"])
    vocals_idx = sources.index("vocals") if "vocals" in sources else 3
    
    # 20s chunks with 2s overlap for smooth crossfade
    chunk_len = 20 * sr
    overlap = 2 * sr
    stride = chunk_len - overlap
    
    out_vocals = torch.zeros((2, total_len), dtype=torch.float32)
    weight_acc = torch.zeros((1, total_len), dtype=torch.float32)
    
    window = torch.ones(chunk_len)
    fade_len = overlap
    fade = torch.linspace(0, 1, fade_len)
    window[:fade_len] = fade
    window[-fade_len:] = torch.flip(fade, dims=[0])
    
    duration_sec = total_len / sr
    print(f"[Demucs] Separating dialogue & action SFX for {duration_sec:.1f}s audio...", flush=True)
    
    start_pos = 0
    with torch.no_grad():
        while start_pos < total_len:
            end_pos = min(start_pos + chunk_len, total_len)
            chunk = waveform[:, start_pos:end_pos]
            cur_len = chunk.shape[1]
            
            if cur_len < chunk_len:
                pad_chunk = torch.zeros((2, chunk_len))
                pad_chunk[:, :cur_len] = chunk
                cur_input = pad_chunk.unsqueeze(0).to(device)
                cur_window = window[:cur_len]
            else:
                cur_input = chunk.unsqueeze(0).to(device)
                cur_window = window
                
            separated = model(cur_input)
            chunk_vocals = separated[0, vocals_idx, :, :cur_len].cpu()
            
            win_2d = cur_window.unsqueeze(0)
            out_vocals[:, start_pos:end_pos] += chunk_vocals * win_2d
            weight_acc[:, start_pos:end_pos] += win_2d
            
            pct = min(100.0, (end_pos / total_len) * 100)
            print(f"[Demucs Progress] {pct:5.1f}% [{end_pos/sr:5.1f}s / {duration_sec:5.1f}s]", flush=True)
            
            start_pos += stride
            
    weight_acc[weight_acc == 0] = 1.0
    out_vocals = out_vocals / weight_acc
    
    # 1. Save Vocals
    vocals_dir = os.path.dirname(output_vocals_path)
    if vocals_dir:
        os.makedirs(vocals_dir, exist_ok=True)
        
    out_np = out_vocals.T.numpy()
    sf.write(output_vocals_path, out_np, sr, subtype="PCM_16")
    print(f"[Demucs] Successfully saved dialogue stem to: {output_vocals_path}", flush=True)
    
    # 2. Save Instrumental & Action SFX (Original minus Vocals)
    if output_no_vocals_path:
        no_vocals_dir = os.path.dirname(output_no_vocals_path)
        if no_vocals_dir:
            os.makedirs(no_vocals_dir, exist_ok=True)
            
        out_no_vocals = waveform - out_vocals
        # Clamp to avoid clipping
        out_no_vocals = torch.clamp(out_no_vocals, -1.0, 1.0)
        no_vocals_np = out_no_vocals.T.numpy()
        sf.write(output_no_vocals_path, no_vocals_np, sr, subtype="PCM_16")
        print(f"[Demucs] Successfully saved action SFX & BGM stem to: {output_no_vocals_path}", flush=True)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Demucs AI Stem Separation for Movie Dubbing")
    parser.add_argument("--input", "-i", required=True, help="Input audio file")
    parser.add_argument("--vocals", "-v", required=True, help="Output vocals/dialogue path (.wav)")
    parser.add_argument("--no-vocals", "-nv", required=False, help="Output action SFX & background path (.wav)")
    
    args = parser.parse_args()
    separate_stems(args.input, args.vocals, args.no_vocals)
