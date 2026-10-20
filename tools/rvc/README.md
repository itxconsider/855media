# Local voice conversion runtime

855Media uses `infer_cli.py` with a project-local Python interpreter and inference
engine. It does not require an external Applio installation or its web UI.

- Interpreter: `<project>/python/python.exe`; override with `MEDIA855_PYTHON`.
- Engine: `tools/rvc/runtime/rvc/`; override its parent with `MEDIA855_RVC_HOME`.
- Selectable voices: `models/voices/<speaker>/*.pth` and the matching `.index`.
- Preserved training work: `assets/voice-training/logs/` and `datasets/`.

The local inference engine was retained from the existing Applio installation.
Its upstream license is preserved as `runtime/LICENSE`. Python, engine weights,
voice weights and training data are ignored by Git; back them up separately.
The `.venv` junction points to the independent project `python` directory.
Invoke Python packages using `python.exe -m ...`: relocated Scripts launchers
may contain the old interpreter path.

To install on another machine, provision the compatible Python dependencies,
the RVC inference source/configs and required contentvec/RMVPE model assets in
the directories above. Copy trained voices separately. A source checkout alone
does not contain these large local assets. Meta speech providers retain their
separate optional environment described in `tools/meta_dubbing/README.md`.
