import contextlib
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

import numpy as np
import soundfile as sf
import torch

import worker


class WorkerTests(unittest.TestCase):
    def test_language_mapping_and_unknown_language(self):
        self.assertEqual(worker.language_id("Khmer"), "khm_Khmr")
        self.assertEqual(worker.language_id("Chinese"), "cmn_Hans")
        self.assertEqual(worker.language_id("eng_Latn"), "eng_Latn")
        self.assertIsNone(worker.language_id("Auto"))
        with self.assertRaises(ValueError):
            worker.language_id("unsupported-language")

    def test_chunks_cover_audio_without_gaps_or_overlong_inputs(self):
        ranges = list(worker.chunk_ranges(16000 * 61 + 1, 16000))
        self.assertEqual(ranges[0][0], 0)
        self.assertEqual(ranges[-1][1], 16000 * 61 + 1)
        for index, (start, end) in enumerate(ranges):
            self.assertLessEqual((end - start) / 16000, 25)
            if index:
                self.assertEqual(start, ranges[index - 1][1])

    def test_silence_produces_no_speech_ranges(self):
        self.assertEqual(worker.speech_ranges(np.zeros(16000), 16000), [])

    def test_transcription_keeps_absolute_timing_and_loads_model_once(self):
        calls = []
        loads = []

        class FakePipeline:
            def __init__(self, model_card):
                loads.append(model_card)

            def transcribe(self, audio, batch_size, **kwargs):
                calls.append((audio[0], batch_size, kwargs))
                return ["spoken dialogue"]

        module = types.ModuleType("omnilingual_asr.models.inference.pipeline")
        module.ASRInferencePipeline = FakePipeline
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "speech.wav"
            waveform = np.zeros(28 * 16000, dtype=np.float32)
            waveform[8000:16000] = 0.2
            waveform[26 * 16000:27 * 16000] = 0.2
            sf.write(path, waveform, 16000)
            with patch.dict(sys.modules, {"omnilingual_asr.models.inference.pipeline": module}):
                result = worker.transcribe({"input": str(path), "language": "Khmer"})
        self.assertEqual(len(loads), 1)
        self.assertEqual(len(result["segments"]), 2)
        self.assertLess(result["segments"][0]["start"], 0.6)
        self.assertGreater(result["segments"][1]["start"], 25)
        self.assertLessEqual(result["segments"][1]["end"], 28)
        self.assertTrue(all(call[2]["lang"] == ["khm_Khmr"] for call in calls))

    def test_separation_keeps_length_stereo_ambience_and_overlap_continuity(self):
        class FakeBatch:
            def __init__(self, audio):
                self.audio = audio

            def to(self, device):
                return self

        class FakeProcessor:
            audio_sampling_rate = 48000

            @classmethod
            def from_pretrained(cls, name):
                return cls()

            def __call__(self, audios, descriptions):
                return FakeBatch(audios[0])

        class FakeModel:
            @classmethod
            def from_pretrained(cls, name):
                return cls()

            def eval(self):
                return self

            def cuda(self):
                return self

            def separate(self, batch, **kwargs):
                # Match the released SAM API: one tensor per item in a list.
                return types.SimpleNamespace(target=[batch.audio.reshape(-1) * 0.5])

        module = types.ModuleType("sam_audio")
        module.SAMAudio = FakeModel
        module.SAMAudioProcessor = FakeProcessor
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            rate = 16000
            time = np.arange(int(22.5 * rate)) / rate
            source = np.stack((0.2 + 0.02 * np.sin(100 * time), 0.1 + 0.01 * np.sin(100 * time)), axis=1)
            sf.write(root / "input.wav", source, rate, subtype="FLOAT")
            with patch.dict(sys.modules, {"sam_audio": module}), \
                 patch.object(torch.cuda, "is_available", return_value=True), \
                 patch.object(torch, "autocast", return_value=contextlib.nullcontext()):
                worker.separate({"input": str(root / "input.wav"),
                                 "vocals": str(root / "vocals.wav"),
                                 "background": str(root / "background.wav")})
            vocals, _ = sf.read(root / "vocals.wav")
            background, _ = sf.read(root / "background.wav")
        self.assertEqual(vocals.shape, source.shape)
        self.assertEqual(background.shape, source.shape)
        np.testing.assert_allclose(vocals + background, source, atol=0.00007)
        np.testing.assert_allclose(background[:, 0] - background[:, 1],
                                   source[:, 0] - source[:, 1], atol=0.00007)


if __name__ == "__main__":
    unittest.main()
