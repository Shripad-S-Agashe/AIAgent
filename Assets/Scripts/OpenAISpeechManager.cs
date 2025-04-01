using UnityEngine;
using System.Threading.Tasks;
using OpenAI;
using NativeWebSocket;

public class OpenAISpeechManager : MonoBehaviour
{
    public MicAudioSender micSender;
    public OpenAIMessageHandler messageHandler;

    private OpenAIApi2 openAI;
    private WebSocket webSocket;
    private bool sessionReady = false;
    private bool isRecording = false;

    private async void Start()
    {
        openAI = new OpenAIApi2(); // assumes auth.json is set up
        await InitializeRealtimeSession();
    }

    private async Task InitializeRealtimeSession()
    {
        Debug.Log("Creating OpenAI Realtime Session...");

        var request = new OpenAIApi2.RealtimeSessionRequest
        {
            model = "gpt-4o-realtime-preview",
            instructions = "You are a helpful assistant.",
            modalities = new string[] { "audio", "text" }
        };

        var response = await openAI.CreateRealtimeSession(request);

        if (response?.Error != null)
        {
            Debug.LogError("Failed to create session: " + response.Error.Message);
            return;
        }

        Debug.Log("Session created. Connecting to WebSocket...");

        webSocket = await openAI.ConnectRealtimeWebSocket(
            onOpenCallback: (ws) => {
                Debug.Log("WebSocket Connected!");
                sessionReady = true;

                // Pass the connection to micSender
                micSender.ws = ws;
            },
            onMessageCallback: (bytes) => {
                // Route audio/text back to handler
                messageHandler.HandleWebSocketMessage(bytes);
            },
            onErrorCallback: (err) => {
                Debug.LogError("WebSocket Error: " + err);
            },
            onCloseCallback: (code) => {
                Debug.LogWarning("WebSocket closed: " + code);
                sessionReady = false;
            }
        );
    }

    private void Update()
    {
        // Required for NativeWebSocket on WebGL (not needed in editor/standalone)
#if !UNITY_EDITOR && UNITY_WEBGL
        webSocket?.DispatchMessageQueue();
#endif

        if (!sessionReady) return;

        // Space held: start recording
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (!isRecording)
            {
                micSender.StartRecording();
                isRecording = true;
                Debug.Log("Recording started");
            }
        }
        // Space released: stop recording
        else if (Input.GetKeyUp(KeyCode.Space))
        {
            if (isRecording)
            {
                micSender.StopRecording();
                isRecording = false;
                Debug.Log("Recording stopped");
            }
        }
    }

    private async void OnApplicationQuit()
    {
        if (webSocket != null)
        {
            await webSocket.Close();
        }
    }
}
