using System.Text.Json;

namespace Bke.MetadataManager.Services;

public sealed record OrganizeBatchRequest(string EventName, string Category, string DestinationFolder, string[] Paths);
public sealed record OrganizeFileResult(string SourcePath, string? DestinationPath, string? Warning, string? Error);
public sealed record OrganizeBatchResponse(List<OrganizeFileResult> Results);

/// <summary>
/// Stable JSON protocol between the macOS-native SwiftUI frontend and the same
/// verified, overwrite-refusing .NET media mover used by Avalonia.
/// Only paths and metadata cross this boundary: no media bytes, re-encoding, or SQL.
/// </summary>
public static class OrganizerBridge
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<string> ProcessJsonAsync(string input, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<OrganizeBatchRequest>(input, Options)
            ?? throw new InvalidDataException("No organization request provided.");
        if (string.IsNullOrWhiteSpace(request.EventName) ||
            string.IsNullOrWhiteSpace(request.Category) ||
            string.IsNullOrWhiteSpace(request.DestinationFolder) ||
            request.Paths is null || request.Paths.Length == 0)
            throw new InvalidDataException("Event, category, destination and paths are required.");

        var mover = new MediaOrganizer();
        List<OrganizeFileResult> results = [];
        foreach (var path in request.Paths.Distinct(StringComparer.Ordinal))
        {
            try
            {
                var output = await mover.MoveAsync(path, request.DestinationFolder, request.EventName, request.Category, ct);
                results.Add(new OrganizeFileResult(path, output.DestinationPath, output.Warning, null));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                results.Add(new OrganizeFileResult(path, null, null, ex.Message));
            }
        }
        return JsonSerializer.Serialize(new OrganizeBatchResponse(results), Options);
    }
}
