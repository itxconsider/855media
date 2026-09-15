using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Dubbing;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("===================================================================");
        Console.WriteLine("🎬 855Media Movie Dubbing Studio - Frame-Accurate Scene Test");
        Console.WriteLine("Movie: Shutter Island - The Drowning Scene");
        Console.WriteLine("===================================================================");

        var inputVideo = @"C:\Users\itxco\Desktop\Shutter Island - The Tragic Loss_ The Drowning Scene 💧💔. #movieclip #movie #film.mp4";
        var outputDir = @"d:\repos\855Media\downloads";
        Directory.CreateDirectory(outputDir);
        var outputVideo = Path.Combine(outputDir, "shutter_island_dubbed_frame_accurate.mp4");
        var ffmpeg = @"d:\repos\855Media\855Media\bin\Debug\net10.0\ffmpeg.exe";

        if (!File.Exists(inputVideo))
        {
            Console.WriteLine($"[ERROR] Input video not found: {inputVideo}");
            return;
        }

        // Actors
        var rvcPth = @"d:\repos\855Media\models\voices\seng_dyna_v3\seng_dyna_v3.pth";
        var rvcIndex = @"d:\repos\855Media\models\voices\seng_dyna_v3\seng_dyna_v3.index";

        var teddyCharacter = new MovieCharacter
        {
            Name = "Teddy (Leonardo)",
            BaseVoice = "km-KH-PisethNeural",
            SpeechRate = "+18%",
            EnableRvc = File.Exists(rvcPth),
            RvcModelPath = rvcPth,
            RvcIndexPath = rvcIndex,
            ColorTag = "#3B82F6"
        };

        var doloresCharacter = new MovieCharacter
        {
            Name = "Dolores (Female)",
            BaseVoice = "km-KH-SreymomNeural",
            SpeechRate = "+12%",
            EnableRvc = false,
            ColorTag = "#EC4899"
        };

        var segments = new (TimeSpan Start, TimeSpan End, string Orig, string Khmer, MovieCharacter Speaker)[]
        {
            (TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(3.0),
             "Dolores... why are you all wet?",
             "ដូឡូរ៉េស... ហេតុអ្វីបានជាអូនទទឹកជោគបែបនេះ?", teddyCharacter),

            (TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(5.5),
             "Where are the kids, Dolores?",
             "ចុះកូនៗនៅឯណា ដូឡូរ៉េស?", teddyCharacter),

            (TimeSpan.FromSeconds(5.8), TimeSpan.FromSeconds(8.0),
             "They're in school.",
             "ពួកគេនៅសាលារៀន។", doloresCharacter),

            (TimeSpan.FromSeconds(8.2), TimeSpan.FromSeconds(11.0),
             "It's Saturday. The school is closed.",
             "ថ្ងៃនេះថ្ងៃសៅរ៍តើ សាលាបិទហើយ។", teddyCharacter),

            (TimeSpan.FromSeconds(11.2), TimeSpan.FromSeconds(14.0),
             "My school isn't. They are at the lake.",
             "សាលារបស់ខ្ញុំបើកតើ ពួកគេនៅបឹង។", doloresCharacter),

            (TimeSpan.FromSeconds(14.5), TimeSpan.FromSeconds(26.5),
             "Wake up! Wake up! Wake up, please!",
             "ភ្ញាក់ឡើង! ភ្ញាក់ឡើងកូន! ភ្ញាក់ឡើង!", teddyCharacter),

            (TimeSpan.FromSeconds(27.0), TimeSpan.FromSeconds(32.0),
             "Please, God! No, please, God!",
             "សូមព្រះមេត្តាផង! ទេ ព្រះអើយ!", teddyCharacter),

            (TimeSpan.FromSeconds(38.0), TimeSpan.FromSeconds(41.0),
             "Let's put them on the table, Andrew.",
             "ចូរយើងដាក់ពួកគេនៅលើតុទៅ Andrew។", doloresCharacter),

            (TimeSpan.FromSeconds(41.2), TimeSpan.FromSeconds(44.5),
             "Let's dry them off.",
             "យើងនឹងជូតខ្លួនឱ្យស្ងួត...", doloresCharacter),

            (TimeSpan.FromSeconds(45.0), TimeSpan.FromSeconds(47.5),
             "We'll put dry clothes on them.",
             "យើងនឹងប្តូរសម្លៀកបំពាក់ស្ងួតឱ្យពួកគេ...", doloresCharacter),

            (TimeSpan.FromSeconds(47.8), TimeSpan.FromSeconds(53.0),
             "Oh, God! Oh, my God, no!",
             "ព្រះអើយ! ព្រះអើយ ទេ!", teddyCharacter),
        };

        var job = new DubbingJob
        {
            VideoFilePath = inputVideo,
            OutputFilePath = outputVideo,
            SourceLanguage = "English",
            SelectedVoice = "km-KH-PisethNeural",
            EnableVoiceCloning = true,
            RvcModelPath = rvcPth,
            RvcIndexPath = rvcIndex,
            PitchShift = 0,
            BgmVolume = 0.35,
            VoiceVolume = 1.0,
            EnableAiStemSeparation = true,
            EnableDynamicDucking = true
        };

        job.Characters.Add(teddyCharacter);
        job.Characters.Add(doloresCharacter);

        for (int i = 0; i < segments.Length; i++)
        {
            var s = segments[i];
            job.Segments.Add(new SubtitleSegment
            {
                Index = i + 1,
                StartTime = s.Start,
                EndTime = s.End,
                OriginalText = s.Orig,
                KhmerText = s.Khmer,
                CharacterId = s.Speaker.Id,
                SpeakerName = s.Speaker.Name,
                SpeakerColor = s.Speaker.ColorTag
            });
        }

        Console.WriteLine($"[INFO] Dubbing {job.Segments.Count} cinematic dialogue lines with frame-accurate timeline mixing...");

        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(job.StatusMessage))
                Console.WriteLine($"[DUBBING] {job.StatusMessage} ({job.Progress:F0}%)");
        };

        var pipeline = new DubbingPipeline();
        try
        {
            await pipeline.ExecuteAsync(job, ffmpeg, CancellationToken.None);

            if (File.Exists(outputVideo))
            {
                var fi = new FileInfo(outputVideo);
                Console.WriteLine("\n===================================================================");
                Console.WriteLine("🎉 FRAME-ACCURATE DUBBING COMPLETE!");
                Console.WriteLine($"Output File : {outputVideo}");
                Console.WriteLine($"File Size   : {fi.Length / 1024.0 / 1024.0:F2} MB");
                Console.WriteLine("===================================================================");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] {ex.Message}");
            Console.WriteLine(job.DetailedLog);
        }
    }
}
