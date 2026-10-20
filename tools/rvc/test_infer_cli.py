"""Validate wrapper relocation without loading GPU models or contacting services."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


class InferenceCliTests(unittest.TestCase):
    def test_relocated_runtime_resolves_relative_media_before_chdir(self):
        with tempfile.TemporaryDirectory(prefix="855 rvc ") as temporary:
            root = Path(temporary)
            engine = root / "independent runtime" / "rvc" / "infer"
            engine.mkdir(parents=True)
            (engine / "infer.py").write_text(
                "from pathlib import Path\n"
                "class VoiceConverter:\n"
                " def convert_audio(self, **kwargs):\n"
                "  assert Path(kwargs['audio_input_path']).is_file()\n"
                "  assert Path(kwargs['model_path']).is_file()\n"
                "  assert Path(kwargs['index_path']).is_file()\n"
                "  Path(kwargs['audio_output_path']).write_bytes(b'RIFF' + b'0' * 120)\n"
            )
            for filename in ("input.wav", "voice.pth", "voice.index"):
                (root / filename).write_bytes(b"test")
            environment = dict(os.environ, MEDIA855_RVC_HOME=str(engine.parents[1]))
            result = subprocess.run(
                [sys.executable, str(Path(__file__).with_name("infer_cli.py")),
                 "--input", "input.wav", "--output", "output.wav",
                 "--model", "voice.pth", "--index", "voice.index"],
                cwd=root, env=environment, capture_output=True, text=True,
            )
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertGreater((root / "output.wav").stat().st_size, 100)

    def test_missing_runtime_reports_actionable_error(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = subprocess.run(
                [sys.executable, str(Path(__file__).with_name("infer_cli.py")),
                 "--input", "input.wav", "--output", "output.wav", "--model", "voice.pth"],
                env=dict(os.environ, MEDIA855_RVC_HOME=temporary),
                capture_output=True, text=True,
            )
            self.assertNotEqual(0, result.returncode)
            self.assertIn("RVC inference runtime missing", result.stderr)


if __name__ == "__main__":
    unittest.main()
