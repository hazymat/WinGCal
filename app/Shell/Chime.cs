using System.IO;
using System.Media;
namespace WinGCal.Shell;

// WinGCal's own notification sounds (SPEC.md › Next event › Sounds): a few notes made here from decaying sine partials, so there are no sound files to ship and the volume is ours, not Windows'
public static class Chime
{
    const int sampleRate = 44100;
    public static readonly (string Key, string Label)[] Sounds = { ("chime", "Chime"), ("marimba", "Marimba"), ("bell", "Bell") };
    // Each sound: its partials (frequency multiple, loudness) and how fast a note dies away (seconds to fall to about a third)
    static (double[] Multiples, double[] Loudness, double Decay) Voice(string sound) => sound switch
    {
        "marimba" => (new[] { 1.0, 4.0, 9.2 }, new[] { 1.0, 0.28, 0.06 }, 0.11),
        "bell" => (new[] { 1.0, 2.0, 2.76, 5.4 }, new[] { 1.0, 0.45, 0.3, 0.14 }, 0.5),
        _ => (new[] { 1.0, 2.0, 3.0 }, new[] { 1.0, 0.22, 0.07 }, 0.3),
    };
    // Appearing: two soft rising notes. At start time: three, brighter and a little quicker
    static (double Frequency, double At)[] Notes(bool atStart) => atStart ? new[] { (783.99, 0.0), (987.77, 0.14), (1174.66, 0.28) } : new[] { (659.25, 0.0), (880.0, 0.19) };

    static SoundPlayer? playing; // kept so the sound isn't collected mid-play
    public static void Play(string sound, bool atStart, int volumePercent)
    {
        if (sound == "" || volumePercent <= 0) return;
        try
        {
            var voice = Voice(sound); var notes = Notes(atStart);
            var seconds = notes[^1].At + voice.Decay * 5 + 0.1; var samples = new double[(int)(seconds * sampleRate)];
            foreach (var note in notes)
                for (var partial = 0; partial < voice.Multiples.Length; partial++)
                {
                    double frequency = note.Frequency * voice.Multiples[partial], loudness = voice.Loudness[partial], decay = voice.Decay / (1 + partial * 0.6); // higher partials die sooner
                    for (var index = (int)(note.At * sampleRate); index < samples.Length; index++)
                    {
                        var time = (double)index / sampleRate - note.At;
                        samples[index] += loudness * Math.Min(1, time / 0.004) * Math.Exp(-time / decay) * Math.Sin(2 * Math.PI * frequency * time); // 4 ms attack: no click
                    }
                }
            var peak = samples.Max(Math.Abs); var gain = peak > 0 ? Math.Pow(Math.Clamp(volumePercent, 0, 100) / 100.0, 2) * 0.85 / peak : 0; // squared: the slider feels even
            var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
            {
                var dataBytes = samples.Length * 2;
                writer.Write("RIFF"u8); writer.Write(36 + dataBytes); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(dataBytes);
                foreach (var sample in samples) writer.Write((short)(sample * gain * short.MaxValue));
            }
            stream.Position = 0; playing?.Dispose(); playing = new SoundPlayer(stream); playing.Play();
        }
        catch (Exception soundError) { CrashReporter.Write("Playing a notification sound", soundError, false); }
    }
}
