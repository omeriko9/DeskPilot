using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopAssist.Llm.Models;

public sealed class StepsResponse
{
    [JsonPropertyName("steps")]
    public List<Step>? Steps { get; set; }

    // While working: false|null; When complete: string summary
    [JsonPropertyName("done")]
    public object? Done { get; set; }
}

public sealed class Step
{
    public string tool { get; set; } = "";
    public JsonElement args { get; set; } // arbitrary object
    public string human_readable_justification { get; set; } = "";
}