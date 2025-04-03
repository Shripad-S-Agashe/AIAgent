using NativeWebSocket; // Make sure NativeWebSocket is imported
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Unity.WebRTC;
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
                configuration ??= new Configuration();
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
        private readonly JsonSerializerSettings jsonSerializerSettings = new()
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
            string json = JsonConvert.SerializeObject(request, jsonSerializerSettings);
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
            using (UnityWebRequest request = UnityWebRequest.Put(path, payload))
            {
                request.method = method;
                request.SetHeaders(Configuration, ContentType.ApplicationJson);
                UnityWebRequestAsyncOperation asyncOperation = request.SendWebRequest();
                while (!asyncOperation.isDone)
                {
                    await Task.Yield();
                }

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

        [Serializable]
        public enum AIVoices
        {
            coral,
            ash,
            sage
        }

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

            /// <summary>
            /// Optional: Voice Model for the AI Agent.
            /// </summary>
            public string voice { get; set; } = AIVoices.coral.ToString();
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
            string path = $"{BASE_PATH}/realtime/sessions";
            byte[] payload = CreatePayload(request);
            return await DispatchRequest<RealtimeSessionResponse>(path, UnityWebRequest.kHttpVerbPOST, payload);
        }

        #endregion

        #region Realtime WebRTC Connection

        /// <summary>
        /// Asynchronously creates an SDP offer by polling the RTCSessionDescriptionAsyncOperation.
        /// </summary>
        /// <param name="peerConnection">The RTCPeerConnection instance.</param>
        /// <returns>A task resolving to the generated RTCSessionDescription.</returns>
        private async Task<RTCSessionDescription> CreateOfferAsync(RTCPeerConnection peerConnection)
        {
            // Use default offer/answer options.
            RTCOfferAnswerOptions options = RTCOfferAnswerOptions.Default;
            RTCSessionDescriptionAsyncOperation op = peerConnection.CreateOffer(ref options);

            // Poll until the operation is finished.
            while (op.keepWaiting)
            {
                await Task.Yield();
            }

            return op.IsError ? throw new Exception("Error creating offer: " + op.Error.message) : op.Desc;
        }

        /// <summary>
        /// Asynchronously sets the local description by polling the RTCSetSessionDescriptionAsyncOperation.
        /// </summary>
        /// <param name="peerConnection">The RTCPeerConnection instance.</param>
        /// <param name="desc">The RTCSessionDescription to set.</param>
        /// <returns>A task that completes when the local description is successfully set.</returns>
        private async Task SetLocalDescriptionAsync(RTCPeerConnection peerConnection, RTCSessionDescription desc)
        {
            RTCSetSessionDescriptionAsyncOperation op = peerConnection.SetLocalDescription(ref desc);

            // Poll until the operation is finished.
            while (op.keepWaiting)
            {
                await Task.Yield();
            }

            if (op.IsError)
            {
                throw new Exception("Error setting local description: " + op.Error.message);
            }
        }

        /// <summary>
        /// Establishes a WebRTC connection using the provided ephemeral token.
        /// Note: A signaling server is required to exchange the SDP offer/answer and ICE candidates.
        /// </summary>
        /// <param name="ephemeralToken">The ephemeral token from realtime session creation.</param>
        /// <returns>
        /// An asynchronous task returning the established <see cref="RTCPeerConnection"/>.
        /// </returns>
        public async Task<RTCPeerConnection> ConnectRealtimeWebRTC(string ephemeralToken)
        {
            Debug.Log($"Establishing WebRTC connection using ephemeral token: {ephemeralToken}");

            // Create a basic RTC configuration with a public STUN server.
            RTCConfiguration config = default;
            config.iceServers = new RTCIceServer[]
            {
        new() { urls = new string[] { "stun:stun.l.google.com:19302" } }
            };

            // Create a new RTCPeerConnection using the configuration.
            RTCPeerConnection peerConnection = new(ref config)
            {
                // Setup event handler for new ICE candidates.
                OnIceCandidate = candidate =>
                {
                    if (candidate != null)
                    {
                        Debug.Log("New ICE Candidate: " + candidate.Candidate);
                        // In a production scenario, send this candidate to your signaling server.
                    }
                },

                // Monitor connection state changes.
                OnConnectionStateChange = state =>
                {
                    Debug.Log("WebRTC Connection State: " + state);
                }
            };

            // Create an SDP offer.
            RTCSessionDescription offerDesc = await CreateOfferAsync(peerConnection);
            // Set the local description.
            await SetLocalDescriptionAsync(peerConnection, offerDesc);

            Debug.Log("WebRTC offer created: " + offerDesc.sdp);

            // In a complete implementation, you would now send the offer (and ephemeral token)
            // to your signaling server, receive an SDP answer, and then call:
            // await SetRemoteDescriptionAsync(peerConnection, answerDesc);

            return peerConnection;
        }

        #endregion

        #region Realtime WebSocket Connection

        /// <summary>
        /// Connects to the realtime API using WebSockets, authenticating with the ephemeral token.
        /// </summary>
        /// <param name="ephemeralToken">The ephemeral token (client secret) from the realtime session.</param>
        /// <param name="onOpenCallback">Callback for when connection opens.</param>
        /// <param name="onErrorCallback">Callback for errors.</param>
        /// <param name="onCloseCallback">Callback for when connection closes.</param>
        /// <param name="onMessageCallback">Callback for messages.</param>
        /// <param name="model">The realtime model ID to connect to (e.g., "gpt-4o-realtime-preview-2024-12-17").</param>
        /// <returns>The connected WebSocket instance.</returns>
        public async Task<WebSocket> ConnectRealtimeWebSocket(
            string ephemeralToken,
            Action<WebSocket> onOpenCallback = null,
            Action<string> onErrorCallback = null,
            Action<WebSocketCloseCode> onCloseCallback = null,
            Action<byte[]> onMessageCallback = null,
            string model = "gpt-4o-realtime-preview-2024-12-17")
            {
            // Ensure an ephemeral token is provided.
            if (string.IsNullOrEmpty(ephemeralToken))
            {
                Debug.LogError("Ephemeral token is not provided!");
                throw new InvalidOperationException("Ephemeral token is not provided.");
            }

            // Construct the WebSocket URL with the model as a query parameter.
            string wsUrl = $"wss://api.openai.com/v1/realtime?model={model}";

            // Use the ephemeral token for authentication.
            Dictionary<string, string> headers = new()
            {
                { "Authorization", $"Bearer {ephemeralToken}" },
                { "OpenAI-Beta", "realtime=v1" }
            };

            WebSocket ws = new(wsUrl, headers);

            // --- Attach event handlers using the callbacks ---
            ws.OnOpen += () =>
            {
                Debug.Log("Internal: WebSocket connection opened.");
                onOpenCallback?.Invoke(ws);
            };
            ws.OnError += (errorMsg) =>
            {
                Debug.LogError("Internal: WebSocket error: " + errorMsg);
                onErrorCallback?.Invoke(errorMsg);
            };
            ws.OnClose += (closeCode) =>
            {
                Debug.Log("Internal: WebSocket closed with code: " + closeCode);
                onCloseCallback?.Invoke(closeCode);
            };
            ws.OnMessage += (bytes) =>
            {
                // Call the passed in callback.
                onMessageCallback?.Invoke(bytes);

               // Debug_DisplayDataAsJasonInLog(bytes);
            };
            Debug.Log($"Attempting WebSocket connection to {wsUrl}...");
            try
            {
                await ws.Connect();
                // Note: Completion of Connect doesn't guarantee that the state is 'Open' immediately.
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocket ws.Connect() exception: {ex.Message}");
                onErrorCallback?.Invoke($"Connection failed: {ex.Message}");
            }

            return ws; // Return the WebSocket instance
        }

        private static void Debug_DisplayDataAsJasonInLog(byte[] bytes)
        {
            string message = Encoding.UTF8.GetString(bytes);
            Debug.Log("Received Raw Message: " + message);

            // (Optionally, keep your JSON parsing here.)
            try
            {
                var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                if (json != null)
                {
                    Debug.Log("Server Event Data: " + JsonConvert.SerializeObject(json, Formatting.Indented));
                    if (json.TryGetValue("event", out object eventType))
                    {
                        Debug.Log("Event Received: " + eventType);
                    }
                    else
                    {
                        Debug.Log("No explicit 'event' field found in the message.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Error parsing received message as JSON: " + ex.Message);
            }
        }

        #endregion

    }
}
