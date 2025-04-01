using NativeWebSocket; // Make sure NativeWebSocket is imported
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace OpenAI
{
    /// <summary>
    /// This class reuses the auth.json-based API key loading as in your original OpenAIApi.
    /// </summary>
    public class OpenAIApi2
    {
        /// <summary>
        /// Reads and sets user credentials from %User%/.openai/auth.json.
        /// </summary>
        private Configuration configuration;

        /// <summary>
        /// Gets the configuration instance. If not already created, initializes a new one.
        /// </summary>
        private Configuration Configuration
        {
            get
            {
                if (configuration == null)
                {
                    configuration = new Configuration();
                }
                return configuration;
            }
        }

        /// <summary>
        /// Base OpenAI API URL.
        /// </summary>
        private const string BASE_PATH = "https://api.openai.com/v1";

        /// <summary>
        /// JSON serializer settings using snake_case for JSON.
        /// </summary>
        private readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver()
            {
                NamingStrategy = new CustomNamingStrategy()
            },
            Culture = CultureInfo.InvariantCulture
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenAIApi2"/> class.
        /// </summary>
        /// <param name="apiKey">Optional API key.</param>
        /// <param name="organization">Optional organization.</param>
        public OpenAIApi2(string apiKey = null, string organization = null)
        {
            if (apiKey != null)
            {
                configuration = new Configuration(apiKey, organization);
            }
        }

        #region Helper Methods

        /// <summary>
        /// Creates a JSON payload from a given request object.
        /// </summary>
        /// <typeparam name="T">Type of the request object.</typeparam>
        /// <param name="request">The request object.</param>
        /// <returns>A byte array representing the JSON payload.</returns>
        private byte[] CreatePayload<T>(T request)
        {
            var json = JsonConvert.SerializeObject(request, jsonSerializerSettings);
            return Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// Dispatches an HTTP request with an optional payload and deserializes the response.
        /// </summary>
        /// <typeparam name="T">The type of the response, which must implement <see cref="IResponse"/>.</typeparam>
        /// <param name="path">The request URL.</param>
        /// <param name="method">The HTTP method.</param>
        /// <param name="payload">Optional payload data.</param>
        /// <returns>The deserialized response object.</returns>
        private async Task<T> DispatchRequest<T>(string path, string method, byte[] payload = null) where T : IResponse
        {
            T data;
            using (var request = UnityWebRequest.Put(path, payload))
            {
                request.method = method;
                request.SetHeaders(Configuration, ContentType.ApplicationJson);
                var asyncOperation = request.SendWebRequest();
                while (!asyncOperation.isDone)
                    await Task.Yield();
                data = JsonConvert.DeserializeObject<T>(request.downloadHandler.text, jsonSerializerSettings);
            }
            if (data?.Error != null)
            {
                ApiError error = data.Error;
                Debug.LogError($"Error Message: {error.Message}\nError Type: {error.Type}\n");
            }
            if (data?.Warning != null)
            {
                Debug.LogWarning(data.Warning);
            }
            return data;
        }

        #endregion

        #region Realtime Session Creation

        /// <summary>
        /// Request object for creating a realtime session.
        /// </summary>
        public class RealtimeSessionRequest
        {
            /// <summary>
            /// Required: the realtime model name.
            /// </summary>
            public string model { get; set; } = "gpt-4o-realtime-preview";

            /// <summary>
            /// Required: the modalities to be used.
            /// </summary>
            public string[] modalities { get; set; } = new string[] { "audio", "text" };

            /// <summary>
            /// Required: instructions for the session.
            /// </summary>
            public string instructions { get; set; } = "You are a friendly assistant.";
        }

        /// <summary>
        /// Response object matching the realtime session response.
        /// </summary>
        public class RealtimeSessionResponse : IResponse
        {
            /// <summary>
            /// The session ID.
            /// </summary>
            public string id { get; set; }

            /// <summary>
            /// The object type.
            /// </summary>
            public string @object { get; set; }

            /// <summary>
            /// The model name.
            /// </summary>
            public string model { get; set; }

            /// <summary>
            /// The modalities.
            /// </summary>
            public string[] modalities { get; set; }

            /// <summary>
            /// The session instructions.
            /// </summary>
            public string instructions { get; set; }

            /// <summary>
            /// The voice setting.
            /// </summary>
            public string voice { get; set; }

            /// <summary>
            /// The input audio format.
            /// </summary>
            public string input_audio_format { get; set; }

            /// <summary>
            /// The output audio format.
            /// </summary>
            public string output_audio_format { get; set; }

            /// <summary>
            /// Input audio transcription settings.
            /// </summary>
            public InputAudioTranscription input_audio_transcription { get; set; }

            /// <summary>
            /// Turn detection settings.
            /// </summary>
            public object turn_detection { get; set; }

            /// <summary>
            /// The available tools.
            /// </summary>
            public string[] tools { get; set; }

            /// <summary>
            /// The tool choice.
            /// </summary>
            public string tool_choice { get; set; }

            /// <summary>
            /// Maximum response output tokens (changed from int to string to allow "inf" values).
            /// </summary>
            public string max_response_output_tokens { get; set; }

            /// <summary>
            /// Temperature setting.
            /// </summary>
            public double temperature { get; set; }

            /// <summary>
            /// Client secret information.
            /// </summary>
            public ClientSecret client_secret { get; set; }

            /// <summary>
            /// Error information.
            /// </summary>
            public ApiError Error { get; set; }

            /// <summary>
            /// Warning message.
            /// </summary>
            public string Warning { get; set; }
        }

        /// <summary>
        /// Represents input audio transcription settings.
        /// </summary>
        public class InputAudioTranscription
        {
            /// <summary>
            /// The transcription model.
            /// </summary>
            public string model { get; set; }
        }

        /// <summary>
        /// Represents a client secret.
        /// </summary>
        public class ClientSecret
        {
            /// <summary>
            /// The secret value.
            /// </summary>
            public string value { get; set; }

            /// <summary>
            /// The expiration timestamp.
            /// </summary>
            public long expires_at { get; set; }
        }

        /// <summary>
        /// Creates a realtime session.
        /// </summary>
        /// <param name="request">The realtime session request object.</param>
        /// <returns>The realtime session response as returned by the API.</returns>
        public async Task<RealtimeSessionResponse> CreateRealtimeSession(RealtimeSessionRequest request)
        {
            // Endpoint for creating realtime sessions.
            var path = $"{BASE_PATH}/realtime/sessions";
            byte[] payload = CreatePayload(request);
            return await DispatchRequest<RealtimeSessionResponse>(path, UnityWebRequest.kHttpVerbPOST, payload);
        }

        #endregion

        #region Realtime WebSocket Connection

        /// <summary>
        /// Connects to the realtime API using WebSockets.
        /// </summary>
        /// <param name="onOpenCallback">Callback for when connection opens.</param>
        /// <param name="onErrorCallback">Callback for errors.</param>
        /// <param name="onCloseCallback">Callback for when connection closes.</param>
        /// <param name="onMessageCallback">Callback for messages.</param>
        /// <param name="model">The realtime model ID to connect to (e.g., "gpt-4o-realtime-preview-2024-12-17").</param>
        /// <returns>The connected WebSocket instance.</returns>
        public async Task<WebSocket> ConnectRealtimeWebSocket(
            Action<WebSocket> onOpenCallback = null,
            Action<string> onErrorCallback = null,
            Action<WebSocketCloseCode> onCloseCallback = null,
            Action<byte[]> onMessageCallback = null,
            string model = "gpt-4o-realtime-preview-2024-12-17")
            {
            // Construct the WebSocket URL with the model as a query parameter.
            string wsUrl = $"wss://api.openai.com/v1/realtime?model={model}";

            // Check Configuration and API Key for robustness.
            if (Configuration?.Auth.ApiKey == null)
            {
                Debug.LogError("OpenAI API Key is not configured!");
                throw new InvalidOperationException("OpenAI API Key is not configured.");
            }

            var headers = new Dictionary<string, string>()
            {
                { "Authorization", $"Bearer {Configuration.Auth.ApiKey}" },
                { "OpenAI-Beta", "realtime=v1" }
            };

            WebSocket ws = new WebSocket(wsUrl, headers);

            // --- Attach event handlers using the callbacks ---
            ws.OnOpen += () =>
            {
                Debug.Log("Internal: WebSocket connection opened.");
                onOpenCallback?.Invoke(ws);
            };
            ws.OnError += (errorMsg) =>
            {
                Debug.LogError("Internal: WebSocket error: " + errorMsg);
                onErrorCallback?.Invoke(errorMsg); // Call the provided callback
            };
            ws.OnClose += (closeCode) =>
            {
                Debug.Log("Internal: WebSocket closed with code: " + closeCode);
                onCloseCallback?.Invoke(closeCode); // Call the provided callback
            };
            ws.OnMessage += (bytes) =>
            {
                // Pass raw bytes to the callback
                onMessageCallback?.Invoke(bytes);
            };

            Debug.Log($"Attempting WebSocket connection to {wsUrl}...");
            try
            {
                await ws.Connect();
                // Note: Connect completion doesn't guarantee state is 'Open' yet,
                // that's why relying on the OnOpen callback is better.
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocket ws.Connect() exception: {ex.Message}");
                onErrorCallback?.Invoke($"Connection failed: {ex.Message}");
            }

            return ws; // Return the WebSocket instance
        }

        #endregion
    }
}
