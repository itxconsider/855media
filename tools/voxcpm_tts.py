"""
VoxCPM2 TTS Engine Runner for 855Media
Generates studio-quality 48kHz speech using OpenBMB's VoxCPM2 model with optional zero-shot voice cloning.
"""

import argparse
import os
import sys
import tempfile
import torch

# Ensure UTF-8 console I/O on Windows
if hasattr(sys.stdout, 'reconfigure') and sys.stdout.encoding and sys.stdout.encoding.lower() != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if hasattr(sys.stderr, 'reconfigure') and sys.stderr.encoding and sys.stderr.encoding.lower() != 'utf-8':
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

try:
    import soundfile as sf
except ImportError:
    sf = None

try:
    from voxcpm import VoxCPM
except ImportError:
    VoxCPM = None


def main():
    parser = argparse.ArgumentParser(description="VoxCPM2 Text-to-Speech Runner for 855Media")
    parser.add_argument("--text", required=True, help="Text to synthesize")
    parser.add_argument("--output", required=True, help="Output audio file path (.wav or .mp3)")
    parser.add_argument("--model", default="openbmb/VoxCPM2", help="Hugging Face model ID or local directory")
    parser.add_argument("--ref-audio", default=None, help="Reference audio file path for zero-shot voice cloning")
    parser.add_argument("--prompt-audio", default=None, help="Prompt audio file for continuation mode")
    parser.add_argument("--prompt-text", default=None, help="Text corresponding to prompt audio")
    parser.add_argument("--timesteps", type=int, default=10, help="Diffusion inference timesteps (default 10)")
    parser.add_argument("--cfg", type=float, default=2.0, help="Guidance scale (default 2.0)")
    parser.add_argument("--device", default="auto", help="Device (cuda / cpu / auto)")

    args = parser.parse_args()

    if VoxCPM is None:
        sys.stderr.write("ERROR: voxcpm library is not installed in this Python environment.\n")
        sys.exit(1)

    if sf is None:
        sys.stderr.write("ERROR: soundfile library is not installed in this Python environment.\n")
        sys.exit(1)

    text = args.text.strip()
    if not text:
        sys.stderr.write("ERROR: Input text is empty.\n")
        sys.exit(1)

    # Determine runtime device
    device = args.device
    if device == "auto":
        device = "cuda" if torch.cuda.is_available() else "cpu"

    print(f"[VoxCPM] Initializing model '{args.model}' on device '{device}'...", file=sys.stderr)

    try:
        model = VoxCPM.from_pretrained(
            args.model,
            optimize=False,
            device=device,
            load_denoiser=True
        )
    except Exception as ex:
        sys.stderr.write(f"ERROR: Failed to load VoxCPM model: {ex}\n")
        sys.exit(2)

    ref_audio = args.ref_audio
    if ref_audio and not os.path.exists(ref_audio):
        print(f"[VoxCPM] Warning: Reference audio '{ref_audio}' not found. Generating default voice.", file=sys.stderr)
        ref_audio = None

    prompt_audio = args.prompt_audio
    prompt_text = args.prompt_text
    if prompt_audio and not os.path.exists(prompt_audio):
        prompt_audio = None
        prompt_text = None

    print(f"[VoxCPM] Synthesizing speech (length: {len(text)} chars, timesteps: {args.timesteps})...", file=sys.stderr)

    try:
        audio = model.generate(
            text=text,
            reference_wav_path=ref_audio,
            prompt_wav_path=prompt_audio,
            prompt_text=prompt_text,
            inference_timesteps=args.timesteps,
            cfg_value=args.cfg
        )
    except Exception as ex:
        sys.stderr.write(f"ERROR: Speech generation failed: {ex}\n")
        sys.exit(3)

    sample_rate = getattr(model.tts_model, "sample_rate", 48000)
    output_path = os.path.abspath(args.output)
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    # If requested format is .mp3, save as temp wav first and convert with ffmpeg
    if output_path.lower().endswith(".mp3"):
        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
            tmp_wav = tmp.name
        try:
            sf.write(tmp_wav, audio, sample_rate)
            # Use ffmpeg to convert to 320k mp3
            import subprocess
            cmd = ["ffmpeg", "-y", "-i", tmp_wav, "-b:a", "320k", output_path]
            subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except Exception:
            # Fallback: rename/write as wav
            output_path = os.path.splitext(output_path)[0] + ".wav"
            sf.write(output_path, audio, sample_rate)
        finally:
            if os.path.exists(tmp_wav):
                try:
                    os.unlink(tmp_wav)
                except OSError:
                    pass
    else:
        sf.write(output_path, audio, sample_rate)

    print(f"[VoxCPM] Successfully generated audio ({sample_rate} Hz) -> {output_path}", file=sys.stderr)


if __name__ == "__main__":
    main()
