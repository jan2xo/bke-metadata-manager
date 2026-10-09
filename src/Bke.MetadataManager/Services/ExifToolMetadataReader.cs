using System.Diagnostics;
using System.Text.Json;
namespace Bke.MetadataManager.Services;
/// <summary>Read-only ExifTool adapter. Metadata writes are deliberately absent.</summary>
public sealed class ExifToolMetadataReader(string executablePath = "exiftool")
{
    public async Task<JsonDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath)) throw new FileNotFoundException("Media file not found.", filePath);
        var start = new ProcessStartInfo { FileName = executablePath, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-json", "-G", "-a", "-s", "--", Path.GetFullPath(filePath) }) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { throw new InvalidOperationException("Could not start ExifTool. Install it or configure its executable path.", ex); }
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"ExifTool failed ({process.ExitCode}): {stderr.Trim()}");
        return JsonDocument.Parse(stdout);
    }
}
