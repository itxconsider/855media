import argparse
import os
import sys
from pathlib import Path

def main():
    parser = argparse.ArgumentParser(description="RVC Inference CLI for 855Media")
    parser.add_argument("--input", required=True, help="Input audio path")
    parser.add_argument("--output", required=True, help="Output audio path")
    parser.add_argument("--model", required=True, help="Path to .pth voice model")
    parser.add_argument("--index", default="", help="Path to .index file")
    parser.add_argument("--pitch", type=int, default=0, help="Pitch shift in semitones")
    parser.add_argument("--f0_method", default="rmvpe", help="Pitch extraction method")
    parser.add_argument("--index_rate", type=float, default=0.75, help="Feature search index rate")
    args = parser.parse_args()

    configured_root = os.environ.get("MEDIA855_RVC_HOME")
    if configured_root:
        runtime_root = Path(configured_root).resolve()
    else:
        candidates = [Path(__file__).resolve().parent / "runtime"]
        for start in (Path(__file__).resolve().parent, Path.cwd()):
            candidates.extend(parent / "tools" / "rvc" / "runtime" for parent in (start, *start.parents))
        runtime_root = next((p for p in candidates if (p / "rvc" / "infer" / "infer.py").is_file()), candidates[0])
    if not (runtime_root / "rvc" / "infer" / "infer.py").is_file():
        print(f"Error: RVC inference runtime missing at {runtime_root}. See tools/rvc/README.md.", file=sys.stderr)
        sys.exit(1)

    # Resolve CLI paths before changing directory for upstream model/config lookup.
    args.input = str(Path(args.input).resolve())
    args.output = str(Path(args.output).resolve())
    args.model = str(Path(args.model).resolve())
    args.index = str(Path(args.index).resolve()) if args.index else ""
    sys.path.insert(0, str(runtime_root))
    os.chdir(runtime_root)

    from rvc.infer.infer import VoiceConverter  # type: ignore

    print(f"[RVC CLI] Converting {args.input} -> {args.output}")
    print(f"[RVC CLI] Model: {args.model}")
    print(f"[RVC CLI] Index: {args.index}")
    print(f"[RVC CLI] Pitch shift: {args.pitch}")

    index_path = args.index if args.index and os.path.exists(args.index) else ""

    VoiceConverter().convert_audio(
        pitch=args.pitch,
        index_rate=args.index_rate,
        volume_envelope=1.0,
        protect=0.33,
        f0_method=args.f0_method,
        audio_input_path=args.input,
        audio_output_path=args.output,
        model_path=args.model,
        index_path=index_path,
        split_audio=False,
        f0_autotune=False,
        f0_autotune_strength=1.0,
        proposed_pitch=False,
        proposed_pitch_threshold=0.0,
        clean_audio=False,
        clean_strength=0.5,
        export_format="WAV",
        embedder_model="contentvec"
    )

    if os.path.exists(args.output) and os.path.getsize(args.output) > 100:
        print("[RVC CLI] Conversion finished successfully!")
        sys.exit(0)
    else:
        print("[RVC CLI] Output file missing or empty!", file=sys.stderr)
        sys.exit(1)

if __name__ == "__main__":
    main()
