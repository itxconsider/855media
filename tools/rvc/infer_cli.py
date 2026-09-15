import argparse
import os
import sys

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

    applio_root = r"C:\Applio-3.6.4"
    if not os.path.exists(applio_root):
        print(f"Error: Applio root not found at {applio_root}", file=sys.stderr)
        sys.exit(1)

    sys.path.insert(0, applio_root)
    os.chdir(applio_root)

    from core import run_infer_script

    print(f"[RVC CLI] Converting {args.input} -> {args.output}")
    print(f"[RVC CLI] Model: {args.model}")
    print(f"[RVC CLI] Index: {args.index}")
    print(f"[RVC CLI] Pitch shift: {args.pitch}")

    index_path = args.index if args.index and os.path.exists(args.index) else ""

    run_infer_script(
        pitch=args.pitch,
        index_rate=args.index_rate,
        volume_envelope=1.0,
        protect=0.33,
        f0_method=args.f0_method,
        input_path=args.input,
        output_path=args.output,
        pth_path=args.model,
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
