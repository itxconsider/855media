"""Optional Meta audio providers. Heavy dependencies load only for the selected task."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import sys


LANGUAGES = {
    "english": "eng_Latn", "en": "eng_Latn",
    "chinese": "cmn_Hans", "zh": "cmn_Hans", "zh-cn": "cmn_Hans",
    "vietnamese": "vie_Latn", "vi": "vie_Latn",
    "thai": "tha_Thai", "th": "tha_Thai",
    "japanese": "jpn_Jpan", "ja": "jpn_Jpan",
    "korean": "kor_Hang", "ko": "kor_Hang",
    "french": "fra_Latn", "fr": "fra_Latn",
    "spanish": "spa_Latn", "es": "spa_Latn",
    "khmer": "khm_Khmr", "km": "khm_Khmr",
    "cantonese": "yue_Hant", "yue": "yue_Hant",
}


def language_id(language):
    language = (language or "auto").strip()
    if language.lower() == "auto":
        return None
    if language in LANGUAGES.values():
        return language
    if language.lower() not in LANGUAGES:
        raise ValueError(f"Unsupported source language: {language}")
    return LANGUAGES[language.lower()]


def chunk_ranges(total_frames, sample_rate, seconds=25):
    """Bound input duration and keep exact absolute offsets for subtitle timing."""
    if sample_rate <= 0 or seconds <= 0:
        raise ValueError("Invalid chunk size")
    size = int(sample_rate * seconds)
    for start in range(0, total_frames, size):
        yield start, min(total_frames, start + size)


def speech_ranges(samples, sample_rate):
    """Energy VAD with pause splitting; output is clip-level timing, not word alignment."""
    import numpy as np
    frame = max(1, int(sample_rate * 0.02))
    rms = np.array([np.sqrt(np.mean(samples[i:i + frame] ** 2))
                    for i in range(0, len(samples), frame)])
    if not len(rms) or rms.max() < 0.0001:
        return []
    threshold = max(0.001, min(float(np.percentile(rms, 20)) * 3, float(rms.max()) * 0.15))
    active = np.flatnonzero(rms > threshold)
    if not len(active):
        return []
    ranges = []
    begin = previous = int(active[0])
    for position in active[1:]:
        position = int(position)
        if position - previous > 18:
            ranges.append((max(0, (begin - 5) * frame), min(len(samples), (previous + 6) * frame)))
            begin = position
        previous = position
    ranges.append((max(0, (begin - 5) * frame), min(len(samples), (previous + 6) * frame)))
    return [(start, end) for start, end in ranges if end - start >= sample_rate * 0.15]


def check_runtime():
    available = {name: importlib.util.find_spec(name) is not None
                 for name in ("torch", "soundfile", "sam_audio", "omnilingual_asr")}
    gpu = "unavailable"
    if available["torch"]:
        import torch
        gpu = torch.cuda.get_device_name(0) if torch.cuda.is_available() else "CPU only"
    return {"summary": f"Python {sys.version.split()[0]}; GPU: {gpu}; " + "; ".join(
        f"{name}: {'installed' if found else 'missing'}" for name, found in available.items())
        + ". Model access and inference are checked on first use."}


def transcribe(request):
    import numpy as np
    import soundfile as sf
    import torch
    from omnilingual_asr.models.inference.pipeline import ASRInferencePipeline

    language = language_id(request.get("language"))
    print("Loading Omnilingual ASR LLM 1B v2 (first use may download model weights)...", flush=True)
    pipeline = ASRInferencePipeline(model_card="omniASR_LLM_1B_v2")
    segments = []
    with sf.SoundFile(request["input"]) as audio:
        if audio.samplerate != 16000 or audio.channels != 1:
            raise ValueError("ASR input must be a 16 kHz mono WAV")
        # Read bounded windows, then split on pauses before sending speech to the model.
        for start, end in chunk_ranges(len(audio), audio.samplerate):
            audio.seek(start)
            waveform = audio.read(end - start, dtype="float32")
            for speech_start, speech_end in speech_ranges(waveform, audio.samplerate):
                speech = np.ascontiguousarray(waveform[speech_start:speech_end])
                kwargs = {"lang": [language]} if language else {}
                output = pipeline.transcribe(
                    [{"waveform": torch.from_numpy(speech), "sample_rate": audio.samplerate}],
                    batch_size=1, **kwargs)
                text = output[0].strip()
                if text:
                    segments.append({"start": (start + speech_start) / audio.samplerate,
                                     "end": (start + speech_end) / audio.samplerate, "text": text})
            print(f"Transcribed {end / audio.samplerate:.1f}/{len(audio) / audio.samplerate:.1f}s", flush=True)
    return {"segments": segments}


def separate(request):
    import numpy as np
    import soundfile as sf
    import torch
    import torchaudio.functional as audio_functional
    from sam_audio import SAMAudio, SAMAudioProcessor

    if not torch.cuda.is_available():
        raise RuntimeError("SAM Audio requires a CUDA GPU in this worker. Use Demucs on this runtime.")
    print("Loading SAM Audio small (requires approved checkpoint access)...", flush=True)
    model = SAMAudio.from_pretrained("facebook/sam-audio-small").eval().cuda()
    processor = SAMAudioProcessor.from_pretrained("facebook/sam-audio-small")
    # BF16 autocast and one candidate keep the initial GPU budget small.
    with sf.SoundFile(request["input"]) as source:
        rate, channels, total = source.samplerate, source.channels, len(source)
        overlap = rate
        stride = 9 * rate
        tail = None
        with sf.SoundFile(request["vocals"], "w", samplerate=rate, channels=channels, subtype="PCM_16") as vocals, \
             sf.SoundFile(request["background"], "w", samplerate=rate, channels=channels, subtype="PCM_16") as background:
            for start in range(0, total, stride):
                source.seek(start)
                original = source.read(min(10 * rate, total - start), dtype="float32", always_2d=True)
                mono = torch.from_numpy(original.mean(axis=1)).unsqueeze(0)
                mono = audio_functional.resample(mono, rate, processor.audio_sampling_rate)
                batch = processor(audios=[mono], descriptions=["speech"]).to("cuda")
                with torch.inference_mode(), torch.autocast("cuda", dtype=torch.bfloat16):
                    result = model.separate(batch, predict_spans=False, reranking_candidates=1)
                # The released implementation returns a list, one tensor per batch item.
                target = result.target[0].detach().float().cpu().reshape(1, -1)
                target = audio_functional.resample(target, processor.audio_sampling_rate, rate).numpy().reshape(-1)
                target = np.pad(target[:len(original)], (0, max(0, len(original) - len(target))))
                if tail is not None:
                    blend_length = min(len(tail), len(target))
                    weight = np.linspace(0, 1, blend_length, dtype=np.float32)
                    target[:blend_length] = tail[:blend_length] * (1 - weight) + target[:blend_length] * weight
                last = start + len(original) >= total
                count = len(original) if last else stride
                speech = np.repeat(target[:count, None], channels, axis=1)
                vocals.write(speech)
                # Subtract the shared dialogue estimate from each original channel to retain stereo ambience.
                background.write(original[:count] - speech)
                tail = target[stride:stride + overlap].copy()
                print(f"Separated {(start + count) / rate:.1f}/{total / rate:.1f}s", flush=True)
                if last:
                    break
    return {"ok": True}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--request", required=True)
    args = parser.parse_args()
    with open(args.request, encoding="utf-8-sig") as stream:
        request = json.load(stream)
    handlers = {"check": check_runtime, "transcribe": transcribe, "separate": separate}
    operation = request["operation"]
    if operation not in handlers:
        raise ValueError(f"Unknown operation: {operation}")
    response = handlers[operation]() if operation == "check" else handlers[operation](request)
    destination = Path(request["response"])
    temporary = destination.with_suffix(".tmp")
    temporary.write_text(json.dumps(response, ensure_ascii=False), encoding="utf-8")
    os.replace(temporary, destination)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"Meta worker failed: {error}", file=sys.stderr, flush=True)
        sys.exit(1)
