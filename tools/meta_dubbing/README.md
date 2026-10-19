# Optional Meta audio providers for Dubbing

855Media can try SAM Audio small for dialogue separation and Omnilingual ASR LLM 1B v2
for transcription. Both are off by default. Select either in the Dubbing audio settings;
the choices and Python path are saved in `.855dub` projects and copied into batch jobs.
Translation, Khmer TTS, RVC, subtitle editing, mixing, and export remain available.

## Setup

Use a separate Python 3.11 or 3.12 environment. Do not install into the Applio/RVC
environment: Meta's packages have their own PyTorch/fairseq2 requirements.
The recommended Windows deployment is a CUDA-enabled WSL2 Linux distribution.
If no distribution is installed, install and initialize one before following these Linux commands.

```bash
sudo apt-get update
sudo apt-get install -y python3-venv python3-dev build-essential libsndfile1 ffmpeg git
python3 -m venv ~/855media-meta
source ~/855media-meta/bin/activate
python -m pip install --upgrade pip
python -m pip install omnilingual-asr soundfile
```

Install a CUDA-enabled PyTorch/fairseq2 combination supported by the current
[fairseq2 installation guide](https://github.com/facebookresearch/fairseq2#installation).
The RTX 5070 Ti requires a PyTorch build supporting its Blackwell architecture.
Check that `python -c 'import torch; print(torch.cuda.is_available())'` prints `True`.

To add SAM Audio to this environment:

```bash
python -m pip install 'git+https://github.com/facebookresearch/sam-audio.git'
python -m pip check
hf auth login
```

Request access to [facebook/sam-audio-small](https://huggingface.co/facebook/sam-audio-small)
using your Hugging Face account first. Accept the model terms yourself. The app does not
submit access requests, collect tokens, or bypass access controls. Keep authentication in
the worker's environment. Model packages can download additional dependencies/checkpoints.

Enter `/home/YOUR_USER/855media-meta/bin/python` in **Meta Python path**. A leading
`/` automatically selects WSL on Windows, using the default WSL distribution.
Files must be on local drives mounted at `/mnt/<drive>`; network/UNC paths are unsupported.
For a working native Windows installation, enter its full `python.exe` path instead.
`MEDIA855_META_PYTHON` is an optional environment-variable fallback.

Click **Check Meta runtime**. It checks Python, GPU discovery, and package availability
without downloading weights. Model access, dependency loading, and actual inference are
validated on first use. The first job may take longer while weights download.

## Runtime behavior

- SAM Audio runs 10-second windows with 1-second crossfades, BF16 autocast, and one
  candidate. The extracted mono dialogue estimate is subtracted from each original
  channel to retain stereo ambience; this is not a guarantee of artifact-free removal.
- Omnilingual ASR loads once per transcription request, processes at most 25 seconds
  at a time, and splits those windows on energy-based speech pauses. Timecodes describe
  the detected speech intervals; they are not word alignment or lip synchronization.
  Continuous speech crossing a window boundary may require manual correction.
- Source-language settings map to Meta language/script IDs; Auto omits conditioning.
  The worker transcribes original speech; it does not translate it into Khmer.
- All Meta workers share a queue in the running app, limiting Meta GPU concurrency to
  one worker. Other applications and non-Meta RVC/Demucs processes can still use VRAM.
- Failure is logged and falls back to the existing separation or Whisper provider.
  If Meta transcription fails and Whisper cannot transcribe, the job fails clearly
  instead of using placeholder dialogue. Cancellation is propagated to the child process.
- Both new providers are optional; neither is required to build or run 855Media.

## Licenses and scope

Omnilingual ASR code/models use Apache 2.0. SAM Audio uses the custom SAM License;
retain required notices when distributing model artifacts. This integration includes
only the runner and does not bundle model weights. SeamlessM4T, NLLB, and MMS TTS
are not included: their noncommercial weights and Khmer speech limitations make them
unsuitable as general replacements for the current production workflow.

Primary references:

- https://github.com/facebookresearch/omnilingual-asr
- https://github.com/facebookresearch/omnilingual-asr/blob/main/src/omnilingual_asr/models/inference/README.md
- https://github.com/facebookresearch/sam-audio
- https://github.com/facebookresearch/sam-audio/blob/main/LICENSE

## Verification

Run the runner tests from the repository root with a Python environment containing
NumPy, soundfile, torch, and torchaudio:

```text
python -m unittest discover -s tools/meta_dubbing -p "test_*.py"
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter FullyQualifiedName~Dubbing
```

The Python tests use synthetic audio and mocked model inference; they do not download
weights. Evaluate real movie clips for retained sound effects, original-dialogue leakage,
transcription accuracy, timing, and peak VRAM before choosing a new default.
