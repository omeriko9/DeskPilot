using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DesktopAssist.Llm.Models;

public sealed class InstructionResponse
{
    [JsonPropertyName("steps")] public List<InstructionStep> Steps { get; init; } = new();
    [JsonPropertyName("done")] public string? Done { get; init; }
}

public sealed class InstructionStep
{
    [JsonPropertyName("function")] public string Function { get; init; } = string.Empty;
    [JsonPropertyName("parameters")] public Dictionary<string, object>? Parameters { get; init; }
    [JsonPropertyName("human_readable_justification")] public string? Justification { get; init; }
}
