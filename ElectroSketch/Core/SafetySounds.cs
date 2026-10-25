using System.Media;
using System.IO;

namespace ElectroSketch.Core;

public static class SafetySounds
{
    private static readonly string ContinuityPath = Path.Combine(Path.GetTempPath(), "ElectroSketch-continuity-v2.wav");
    private static readonly string ShockPath = Path.Combine(Path.GetTempPath(), "ElectroSketch-safety.wav");
    private static SoundPlayer? _continuityPlayer;
    private static SoundPlayer? _shockPlayer;
    public static bool IsContinuityPlaying { get; private set; }

    public static void Continuity()
    {
        try
        {
            Ensure(ContinuityPath, 820, 500, 0.055, false);
            _continuityPlayer ??= new SoundPlayer(ContinuityPath);
            _continuityPlayer.Load();
            _continuityPlayer.PlayLooping();
            IsContinuityPlaying = true;
        }
        catch { IsContinuityPlaying = false; SystemSounds.Beep.Play(); }
    }

    public static void StopContinuity()
    {
        try { _continuityPlayer?.Stop(); } catch { }
        IsContinuityPlaying = false;
    }

    public static void Shock() => Play(ShockPath, 130, 420, 0.16, true, ref _shockPlayer);

    private static void Play(string path, int frequency, int milliseconds, double amplitude, bool sweep, ref SoundPlayer? player)
    {
        try
        {
            Ensure(path, frequency, milliseconds, amplitude, sweep);
            player ??= new SoundPlayer(path);
            player.Load();
            player.Play();
        }
        catch { SystemSounds.Hand.Play(); }
    }

    private static void Ensure(string path, int frequency, int milliseconds, double amplitude, bool sweep)
    {
        if (!File.Exists(path)) File.WriteAllBytes(path, CreateWave(frequency, milliseconds, amplitude, sweep));
    }

    private static byte[] CreateWave(int frequency, int milliseconds, double amplitude, bool sweep)
    {
        const int rate = 22050;
        var samples = rate * milliseconds / 1000;
        var data = new byte[44 + samples * 2];
        using var writer = new BinaryWriter(new MemoryStream(data));
        writer.Write("RIFF"u8.ToArray()); writer.Write(data.Length - 8); writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8.ToArray()); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            var f = sweep ? frequency + 1500.0 * i / samples : frequency;
            var fade = sweep ? Math.Min(1, (samples - i) / (rate * 0.06)) : 1;
            writer.Write((short)(Math.Sin(2 * Math.PI * f * i / rate) * short.MaxValue * amplitude * fade));
        }
        return data;
    }
}
