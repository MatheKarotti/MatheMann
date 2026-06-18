using System;
using System.IO;
using NAudio.Wave;

namespace MatheMann;

/// <summary>
/// Plays a custom MP3 (sounds/open.mp3, shipped next to the plugin DLL).
/// Fire-and-forget; overlapping calls each get their own reader/output which is
/// disposed when playback ends.
/// </summary>
public static class SoundPlayer
{
    private static string? mp3Path;
    private static bool    pathResolved;

    public static void Play(float volume = 1.0f)
    {
        try
        {
            var path = GetMp3Path();
            Plugin.Log.Information($"[MatheMann] Attempting to play sound: {path}");

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
            Plugin.Log.Information("[MatheMann] Playback started.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[MatheMann] Could not play sound (exception).");
        }
    }

    /// <summary>
    /// Locate open.mp3 next to the plugin assembly. Uses Dalamud's reliable
    /// AssemblyLocation rather than Assembly.Location (which can be empty when
    /// the plugin is loaded from a byte array).
    /// </summary>
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

            Plugin.Log.Information($"[MatheMann] Resolved sound path: {mp3Path}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[MatheMann] Failed to resolve mp3 path.");
        }

        return mp3Path;
    }
}
