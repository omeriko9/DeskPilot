using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace DesktopAssist.Llm.Models
{
    /// <summary>
    /// Base class encapsulating shared HttpClient + event plumbing for OpenAI-based clients.
    /// </summary>
    public abstract class OpenAIClientBase : LLMClient
    {
    protected readonly HttpClient Http;
    private string _model;
        protected readonly string BaseUrl;

        public event EventHandler<string>? Info;
        public event EventHandler<string>? Error;

        protected OpenAIClientBase(string baseUrl, string apiKey, string model, TimeSpan? timeout = null)
        {
            BaseUrl = baseUrl.TrimEnd('/');
            _model = model;
            Http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(90) };
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public string CurrentModel => _model;
        public void SetModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _model = model.Trim();
            }
        }

        protected void RaiseInfo(string msg) => Info?.Invoke(this, msg);
        protected void RaiseError(string msg) => Error?.Invoke(this, msg);

        public abstract Task<string?> GetAIResponseAsync(LlmRequest request, CancellationToken ct = default);
    }
}
