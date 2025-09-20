using System;

namespace DesktopAssist.Util;

/// <summary>
/// Lightweight logging helper to standardize console output.
/// (Could be upgraded later to structured logging provider.)
/// </summary>
internal static class Log
{
    public static void Info(string ctx, string message) =>
        Console.WriteLine($"[{ctx}] {message}");

    public static void Warn(string ctx, string message) =>
        Console.WriteLine($"[{ctx}][Warn] {message}");

    public static void Error(string ctx, string message) =>
        Console.WriteLine($"[{ctx}][Error] {message}");

    public static void Error(string ctx, Exception ex, string? message = null) =>
        Console.WriteLine($"[{ctx}][Error] {message} {ex.Message}".Trim());
}
