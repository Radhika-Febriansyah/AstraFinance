using System.Buffers.Binary;
using AstraFinance.Core.Enums;

namespace AstraFinance.App.Services;

/// <summary>
/// Renders the short UI tones as 16-bit mono PCM WAV in memory: two soft sine notes with a gentle
/// overtone and a smooth decay. Generated in code, so there are no audio files or third-party assets.
/// </summary>
public static class ToneSynthesizer
{
    public const int SampleRate = 44100;
    private const int HeaderSize = 44;

    /// <summary>Peak level at 100 % volume (deliberately quiet: these are subtle cues).</summary>
    private const double MaxLevel = 0.35;

    private readonly record struct Note(double Frequency, int Milliseconds, double Gain, double Overtone);

    private static IReadOnlyList<Note> NotesFor(UiSound sound) => sound switch
    {
        // Rising pair: confirmation.
        UiSound.PinSuccess => [new(659.25, 70, 0.9, 0.12), new(987.77, 130, 1.0, 0.12)],
        // Falling, lower pair with a little more overtone: "not quite".
        UiSound.PinError => [new(440.00, 90, 1.0, 0.25), new(349.23, 150, 0.9, 0.25)],
        // Bell-like pair: something needs attention.
        UiSound.ImportantNotice => [new(880.00, 70, 0.8, 0.30), new(1318.51, 180, 1.0, 0.30)],
        // Light, quick tick: saved.
        _ => [new(1046.50, 50, 0.6, 0.10), new(1567.98, 90, 0.7, 0.10)],
    };

    /// <summary>The WAV file bytes for <paramref name="sound"/> at <paramref name="volume"/> (0–100).</summary>
    public static byte[] Render(UiSound sound, int volume)
    {
        var level = Math.Clamp(volume, 0, 100) / 100.0 * MaxLevel;
        var notes = NotesFor(sound);
        var gapSamples = SampleRate * 12 / 1000;
        var tailSamples = SampleRate * 30 / 1000;
        var total = notes.Sum(n => SampleRate * n.Milliseconds / 1000) + gapSamples * (notes.Count - 1) + tailSamples;

        var samples = new short[total];
        var position = 0;
        foreach (var note in notes)
        {
            var count = SampleRate * note.Milliseconds / 1000;
            var attack = SampleRate * 4 / 1000;
            for (var i = 0; i < count; i++)
            {
                var t = (double)i / SampleRate;
                var envelope = i < attack ? (double)i / attack : Math.Exp(-3.5 * (i - attack) / (double)(count - attack));
                var wave = Math.Sin(2 * Math.PI * note.Frequency * t)
                           + note.Overtone * Math.Sin(2 * Math.PI * 2 * note.Frequency * t);
                var value = wave / (1 + note.Overtone) * envelope * note.Gain * level;
                samples[position + i] = (short)Math.Round(Math.Clamp(value, -1, 1) * short.MaxValue);
            }

            position += count + gapSamples;
        }

        return ToWav(samples);
    }

    private static byte[] ToWav(short[] samples)
    {
        var dataBytes = samples.Length * 2;
        var wav = new byte[HeaderSize + dataBytes];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], HeaderSize - 8 + dataBytes);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);              // fmt chunk size
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);               // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);               // mono
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * 2);  // byte rate
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);               // block align
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);              // bits per sample
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(span[(HeaderSize + i * 2)..], samples[i]);
        }

        return wav;
    }
}
