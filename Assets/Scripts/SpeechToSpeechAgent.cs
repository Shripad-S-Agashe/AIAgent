using NativeWebSocket;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace OpenAI
{
    /// <summary>
    /// This class handles speech-to-speech functionality by creating a realtime session,
    /// storing the session secret, and then connecting with WebSocket to the realtime API.
    /// </summary>
    public class SpeechToSpeech : MonoBehaviour
    {
        /// <summary>
        /// Gets the session secret retrieved from the realtime session.
        /// </summary>
        public string SessionSecret { get; private set; }

        private OpenAIApi2 openAIApi;
        private WebSocket ws;

        private async void Start()
        {
            openAIApi = new OpenAIApi2();
            await InitializeSessionAsync();
            await ConnectToWebSocketAsync();
        }

        /// <summary>
        /// Initializes the realtime session, stores the session secret, and prints it in the debug log.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task InitializeSessionAsync()
        {
            Debug.Log("Creating OpenAI Realtime Session...");

            OpenAIApi2.RealtimeSessionRequest request = new()
            {
                model = "gpt-4o-realtime-preview",
                instructions = "You are a friendly assistant.",
                modalities = new string[] { "audio", "text" }
            };

            OpenAIApi2.RealtimeSessionResponse response = await openAIApi.CreateRealtimeSession(request);

            if (response?.client_secret != null)
            {
                SessionSecret = response.client_secret.value;
                Debug.Log("Session Secret: " + SessionSecret);
            }
            else
            {
                Debug.LogError("Failed to create realtime session or retrieve session secret.");
            }
        }

        /// <summary>
        /// Connects to the realtime API via WebSocket using the ephemeral token for authentication.
        /// Mirrors the JavaScript example provided by OpenAI.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ConnectToWebSocketAsync()
        {
            Debug.Log("Connecting to OpenAI realtime WebSocket...");

            // Use the ephemeral token (SessionSecret) for authentication.
            ws = await openAIApi.ConnectRealtimeWebSocket(
                ephemeralToken: SessionSecret,
                onOpenCallback: (wsInstance) =>
                {
                    Debug.Log("Connected to server.");
                },
                onMessageCallback: (bytes) =>
                {
                    // Decode the message from bytes and log it.
                    string message = Encoding.UTF8.GetString(bytes);
                    Debug.Log("Received Message: " + message);
                },
                onErrorCallback: (errorMsg) =>
                {
                    Debug.LogError("WebSocket Error: " + errorMsg);
                },
                onCloseCallback: (closeCode) =>
                {
                    Debug.Log("WebSocket closed with code: " + closeCode);
                },
                model: "gpt-4o-realtime-preview-2024-12-17" // or whichever model you wish to use.
            );
        }
    }
}
