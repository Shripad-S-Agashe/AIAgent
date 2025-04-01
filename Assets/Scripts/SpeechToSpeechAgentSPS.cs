using NativeWebSocket; // The WebSocket library used in OpenAIApi2
using OpenAI; // Your OpenAI namespace
using System; // For Action
using System.Text; // For encoding test messages
using UnityEngine;

/// <summary>
/// MonoBehaviour that tests the WebSocket functionality from OpenAIApi2 and sends microphone audio (PCM16) when Space is held.
/// </summary>
public class SpeechToSpeechAgentSPS : MonoBehaviour
{
    /// <summary>
    /// An instance of OpenAIApi2 that manages API calls.
    /// </summary>
    public OpenAIApi2 openAIApiInstance;

    /// <summary>
    /// The active WebSocket connection.
    /// </summary>
    private WebSocket webSocketConnection;

    /// <summary>
    /// Indicates whether the WebSocket is connected. This flag is set by callbacks.
    /// </summary>
    private bool isConnected = false;

    /// <summary>
    /// The recorded AudioClip from the default microphone.
    /// </summary>
    private AudioClip recordedClip = null;

    /// <summary>
    /// Flag indicating if the microphone is currently recording.
    /// </summary>
    private bool isRecording = false;

    /// <summary>
    /// Unity Awake callback. Initializes the OpenAIApi2 instance.
    /// </summary>
    private void Awake()
    {
        // Consider changing this instantiation method later (e.g., make OpenAIApi2 a MonoBehaviour/Singleton)
        // Ensure API key configuration happens correctly here or is passed if needed.
        try
        {
            // Example: Load API key securely if OpenAIApi2 constructor doesn't handle it.
            // string apiKey = LoadApiKeySecurely();
            // openAIApiInstance = new OpenAIApi2(apiKey);
            openAIApiInstance = new OpenAIApi2(); // Assuming constructor/config handles key for now.
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to initialize OpenAIApi2 in Awake: {ex.Message}");
            openAIApiInstance = null;
        }
    }

    /// <summary>
    /// Unity Start callback. Initiates the WebSocket connection via OpenAIApi2.
    /// </summary>
    async void Start()
    {
        Debug.Log($"Start - Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}"); // ADD THIS
        if (openAIApiInstance == null)
        {
            Debug.LogError("OpenAIApi2 instance is null or failed to initialize!");
            return;
        }

        Debug.Log("SpeechToSpeechAgentSPS: Attempting to connect WebSocket via OpenAIApi2...");
        try
        {
            // Pass our local handler methods as callbacks.
            webSocketConnection = await openAIApiInstance.ConnectRealtimeWebSocket(
                onOpenCallback: HandleWebSocketOpen,
                onErrorCallback: HandleWebSocketError,
                onCloseCallback: HandleWebSocketClose,
                onMessageCallback: HandleWebSocketMessage
            );

            // We no longer primarily rely on checking state immediately after await.
            // The HandleWebSocketOpen callback will set isConnected correctly.
        }
        catch (Exception ex)
        {
            // Catch potential exceptions from ConnectRealtimeWebSocket itself (e.g., config error).
            Debug.LogError($"Failed to initiate WebSocket connection process: {ex.Message}");
            webSocketConnection = null;
        }
    }

    /// <summary>
    /// Unity Update callback. Processes incoming WebSocket messages and handles microphone recording.
    /// </summary>
    void Update()
    {
        // Keep NativeWebSocket message queue pumping.
#if !UNITY_WEBGL || UNITY_EDITOR
        if (webSocketConnection != null)
        {
            // Dispatch needs to happen regardless of connection state to process potential close or error messages.
            webSocketConnection.DispatchMessageQueue();
        }
#endif

        // When Space is pressed down, start recording from the default microphone.
        if (Input.GetKeyDown(KeyCode.Space))
        {
            StartRecordingMic();
        }

        // When Space is released, stop recording and send the recorded audio as PCM16 data.
        if (Input.GetKeyUp(KeyCode.Space))
        {
            StopRecordingAndSendMicData();
        }
    }

    #region WebSocket Event Handlers

    /// <summary>
    /// Callback invoked when the WebSocket connection is successfully opened.
    /// </summary>
    private void HandleWebSocketOpen(WebSocket webSocket)
    {
        Debug.Log("SpeechToSpeechAgentSPS: WebSocket Opened (Callback Received)");
        isConnected = true;
        webSocketConnection = webSocket;
    }

    /// <summary>
    /// Callback invoked when there is an error in the WebSocket connection.
    /// </summary>
    /// <param name="errorMsg">The error message.</param>
    private void HandleWebSocketError(string errorMsg)
    {
        Debug.LogError($"SpeechToSpeechAgentSPS: WebSocket Error (Callback Received): {errorMsg}");
        isConnected = false;
    }

    /// <summary>
    /// Callback invoked when the WebSocket connection is closed.
    /// </summary>
    /// <param name="closeCode">The WebSocket close code.</param>
    private void HandleWebSocketClose(WebSocketCloseCode closeCode)
    {
        Debug.Log($"SpeechToSpeechAgentSPS: WebSocket Closed (Callback Received): {closeCode}");
        isConnected = false;
        webSocketConnection = null; // Ensure we don't try to use a closed connection.
    }

    /// <summary>
    /// Callback invoked when a message is received from the WebSocket.
    /// </summary>
    /// <param name="bytes">The received message as a byte array.</param>
    private void HandleWebSocketMessage(byte[] bytes)
    {
        string message = Encoding.UTF8.GetString(bytes);
        Debug.Log($"SpeechToSpeechAgentSPS: Message Received (Callback): {message}");
        // TODO: Add JSON parsing and actual logic based on OpenAI response.
    }

    /// <summary>
    /// Sends a ping message to keep the WebSocket connection alive.
    /// </summary>
    private async void SendPing()
    {
        if (isConnected && webSocketConnection != null)
        {
            byte[] pingMessage = Encoding.UTF8.GetBytes("ping");
            Debug.Log("Ping sent to keep connection alive.");
            await webSocketConnection.Send(pingMessage);
        }
    }

    #endregion

    #region Microphone Recording and Sending

    /// <summary>
    /// Starts recording audio from the default microphone.
    /// </summary>
    private void StartRecordingMic()
    {
        if (Microphone.devices.Length > 0)
        {
            // Use the first available microphone.
            string device = Microphone.devices[0];
            int frequency = 16000; // Sample rate for PCM16.
            // Record for a maximum of 10 seconds (adjust length as needed) without looping.
            recordedClip = Microphone.Start(device, false, 10, frequency);
            isRecording = true;
            Debug.Log("Started recording from microphone.");
        }
        else
        {
            Debug.LogError("No microphone detected.");
        }
    }

    /// <summary>
    /// Stops recording from the microphone, converts the audio to PCM16, and sends it over the WebSocket.
    /// </summary>
    private void StopRecordingAndSendMicData()
    {
        if (isRecording && recordedClip != null)
        {
            Microphone.End(null); // Stop recording.
            isRecording = false;

            // Convert the recorded AudioClip to float samples.
            float[] samples = new float[recordedClip.samples * recordedClip.channels];
            recordedClip.GetData(samples, 0);

            // Convert float samples to PCM16 byte array.
            byte[] pcm16Data = ConvertFloatToPCM16(samples);

            if (isConnected && webSocketConnection != null)
            {
                webSocketConnection.Send(pcm16Data);
                Debug.Log($"Sent recorded audio data ({pcm16Data.Length} bytes).");
            }
            else
            {
                Debug.LogWarning("Cannot send audio data, WebSocket not open/connected.");
            }
            recordedClip = null;
        }
    }

    /// <summary>
    /// Converts an array of float samples (-1.0 to 1.0) to a byte array in PCM16 format.
    /// </summary>
    /// <param name="samples">The float samples to convert.</param>
    /// <returns>A byte array in PCM16 little-endian format.</returns>
    private byte[] ConvertFloatToPCM16(float[] samples)
    {
        short[] intData = new short[samples.Length];
        byte[] bytesData = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            // Convert the float sample to a short in the range of -32768 to 32767.
            short s = (short)Mathf.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            intData[i] = s;
            // Convert the short to bytes (little-endian) and copy into the byte array.
            byte[] byteArr = BitConverter.GetBytes(s);
            byteArr.CopyTo(bytesData, i * 2);
        }
        return bytesData;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Closes the WebSocket connection asynchronously.
    /// </summary>
    async void CloseWebSocketConnection()
    {
        if (webSocketConnection != null)
        {
            var socketToClose = webSocketConnection; // Capture instance.
            webSocketConnection = null; // Prevent further use immediately.
            isConnected = false; // Set state.

            Debug.Log("Attempting to close WebSocket connection...");
            try
            {
                await socketToClose.Close();
                Debug.Log("WebSocket Close() called.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception during WebSocket Close: {ex.Message}");
            }
        }
        isConnected = false;
    }

    /// <summary>
    /// Unity OnDestroy callback. Ensures cleanup of the WebSocket connection.
    /// </summary>
    void OnDestroy()
    {
        CloseWebSocketConnection();
    }

    #endregion
}
