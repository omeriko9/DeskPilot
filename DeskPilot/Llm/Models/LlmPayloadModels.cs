using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DesktopAssist.Llm.Models
{
    // Root request for the OpenAI responses endpoint (model-agnostic shape for future reuse)
    public sealed class LlmInferenceRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("input")] public List<LlmRoleMessage> Input { get; set; } = new();
    }

    public sealed class LlmRoleMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = string.Empty; // system / user / assistant
        // Use List<object> to ensure System.Text.Json includes runtime properties (text, image_url) without extra polymorphic setup.
        [JsonPropertyName("content")] public List<object> Content { get; set; } = new();
    }

    public abstract class LlmContentPart
    {
        // concrete serialized field; NO capitalized public property name mismatch.
        [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
    }

    public sealed class LlmTextContentPart : LlmContentPart
    {
        public LlmTextContentPart() => Type = "input_text";
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    }

    public sealed class LlmImageContentPart : LlmContentPart
    {
        public LlmImageContentPart() => Type = "input_image";
        [JsonPropertyName("image_url")] public string ImageUrl { get; set; } = string.Empty; // data:image/png;base64,<...>
    }
}
