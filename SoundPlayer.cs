using System;
using System.IO;
using NAudio.Wave;

namespace MatheMann;

// Plays sounds/open.mp3 (shipped next to the DLL). Fire-and-forget; each call gets
// its own reader/output, disposed when playback ends.
public static class SoundPlayer
{
    private static string? mp3Path;
    private static bool    pathResolved;

    public static void Play(float volume = 1.0f)
    {
        try
        {
            var path = GetMp3Path();
            Plugin.Log.Debug($"[MatheMann] Attempting to play sound: {path}");

            if (path is null || !File.Exists(path))
            {
                Plugin.Log.Warning($"[MatheMann] Sound file NOT FOUND at: {path}");
                return;
            }

            var reader = new AudioFileReader(path) { Volume = Math.Clamp(volume, 0f, 1f) };
            var output = new WaveOutEvent();

            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null)
                    Plugin.Log.Warning($"[MatheMann] Playback error: {args.Exception.Message}");
                output.Dispose();
                reader.Dispose();
            };

            output.Init(reader);
            output.Play();
            Plugin.Log.Debug("[MatheMann] Playback started.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[MatheMann] Could not play sound (exception).");
        }
    }

    // Use AssemblyLocation, not Assembly.Location (can be empty when loaded from bytes).
    private static string? GetMp3Path()
    {
        if (pathResolved) return mp3Path;
        pathResolved = true;

        try
        {
            var asmPath = Plugin.PluginInterface.AssemblyLocation.FullName;
            var dir     = Path.GetDirectoryName(asmPath);
            if (dir is not null)
                mp3Path = Path.Combine(dir, "sounds", "open.mp3");

            Plugin.Log.Debug($"[MatheMann] Resolved sound path: {mp3Path}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[MatheMann] Failed to resolve mp3 path.");
        }

        return mp3Path;
    }
}
