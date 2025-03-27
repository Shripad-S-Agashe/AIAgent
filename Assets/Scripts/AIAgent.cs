using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace OpenAI
{
    public interface IAIService
    {
        Task<T> SendRequest<T>(string endpoint, string method, object payload) where T : IResponse;
        Task<CreateChatCompletionResponse> CreateChatCompletion(CreateChatCompletionRequest request);
        // Additional API method signatures can be declared here.
    }

    public class AIAgent : IAIService
    {
        private readonly AIAgentSettings _settings;
        private readonly JsonSerializerSettings _jsonSettings;

        public AIAgent(AIAgentSettings settings)
        {
            _settings = settings;
            _settings.LoadCredentialsFromFile();  // Load the API key from file if not provided.
            _jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy()
                }
            };
        }

        public async Task<T> SendRequest<T>(string endpoint, string method, object payload) where T : IResponse
        {
            string url = $"{_settings.baseUrl}{endpoint}";
            byte[] bodyData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload, _jsonSettings));

            using (var request = new UnityWebRequest(url, method))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyData);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {_settings.GetApiKey()}");
                if (!string.IsNullOrEmpty(_settings.GetOrganization()))
                {
                    request.SetRequestHeader("OpenAI-Organization", _settings.GetOrganization());
                }

                await request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Request error: {request.error}");
                    return default;
                }

                string json = request.downloadHandler.text;
                T result = JsonConvert.DeserializeObject<T>(json, _jsonSettings);
                return result;
            }
        }

        public async Task<CreateChatCompletionResponse> CreateChatCompletion(CreateChatCompletionRequest request)
        {
            const string endpoint = "/chat/completions";
            return await SendRequest<CreateChatCompletionResponse>(endpoint, UnityWebRequest.kHttpVerbPOST, request);
        }

        // Additional API methods (image, audio, etc.) can be implemented in a similar manner.
    }
}
