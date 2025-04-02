using NativeWebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace OpenAI
{
    /// <summary>
    /// This class handles speech-to-speech functionality by creating a realtime session,
    /// storing the session secret, connecting with WebSocket to the realtime API,
    /// and sending audio events (append and commit) from the microphone.
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
        /// <returns>A task representing the asynchronous operation.</returns>
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
                onOpenCallback: async (wsInstance) =>
                {
                    Debug.Log("Connected to server.");
                    ws = wsInstance;
                    // Once connected, record and send mic audio.
                    await SendMicAudioMessage();
                },
                onMessageCallback: (bytes) =>
                {
                    // Decode the message from bytes and log it.
                    string message = Encoding.UTF8.GetString(bytes);
                    Debug.Log("Received Message: " + message);

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
                },
                onErrorCallback: (errorMsg) =>
                {
                    Debug.LogError("WebSocket Error: " + errorMsg);
                },
                onCloseCallback: (closeCode) =>
                {
                    Debug.Log("WebSocket closed with code: " + closeCode);
                },
                model: "gpt-4o-realtime-preview-2024-12-17" // Use your desired realtime model.
            );
        }

        /// <summary>
        /// Records audio from the default microphone for a short duration,
        /// converts it to PCM16 (Base64 encoded), sends it as an input_audio_buffer.append event,
        /// and then sends a commit event to prompt the server to process the audio.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task SendMicAudioMessage()
        {
            string micName = (Microphone.devices.Length > 0) ? Microphone.devices[0] : null;
            if (micName == null)
            {
                Debug.LogError("No microphone found!");
                return;
            }

            // Get the microphone frequency capabilities.
            Microphone.GetDeviceCaps(micName, out int minFreq, out int maxFreq);
            int sampleRate = (maxFreq == 0) ? 44100 : maxFreq;
            int recordLength = 1; // Record for 1 second

            Debug.Log("Recording audio from mic: " + micName);
            AudioClip clip = Microphone.Start(micName, false, recordLength, sampleRate);
            // Wait until recording starts.
            while (Microphone.GetPosition(micName) <= 0)
            {
                await Task.Yield();
            }
            // Wait for the duration of the recording.
            await Task.Delay(recordLength * 1000);
            Microphone.End(micName);
            Debug.Log("Recording complete.");

            // Retrieve the recorded samples.
            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            // Convert float samples (-1 to 1) to 16-bit PCM data.
            short[] intData = new short[samples.Length];
            byte[] bytesData = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                intData[i] = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                byte[] byteArr = BitConverter.GetBytes(intData[i]);
                byteArr.CopyTo(bytesData, i * 2);
            }

            // Confirm PCM16 conversion.
            Debug.Log("Audio converted to PCM16 with " + bytesData.Length + " bytes.");

            // Encode the PCM16 byte array to Base64.
            string base64Audio = Convert.ToBase64String(bytesData);

            // Create the payload for appending the audio buffer.
            var appendPayload = new Dictionary<string, object>
            {
                { "event_id", "event_" + Guid.NewGuid().ToString("N") },
                { "type", "input_audio_buffer.append" },
                { "audio", base64Audio }
            };

            string appendJson = JsonConvert.SerializeObject(appendPayload);
            Debug.Log("Sending mic audio append event: PayLoad Length" + appendJson.Length);

            if (ws == null)
            {
                Debug.LogError("WebSocket instance is null. Cannot send message.");
                return;
            }

            try
            {
                await ws.SendText(appendJson);
            }
            catch (Exception ex)
            {
                Debug.LogError("Error sending mic audio append event: " + ex.Message);
            }

            // Wait a brief moment before sending the commit.
            await Task.Delay(500);
            await SendAudioCommitMessage();
        }

        /// <summary>
        /// Sends an input_audio_buffer.commit event over the WebSocket to prompt the server to process the audio.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task SendAudioCommitMessage()
        {
            var commitPayload = new Dictionary<string, object>
            {
                { "event_id", "event_" + Guid.NewGuid().ToString("N") },
                { "type", "input_audio_buffer.commit" }
            };

            string commitJson = JsonConvert.SerializeObject(commitPayload);
            Debug.Log("Sending audio buffer commit event: " + commitJson);

            try
            {
                await ws.SendText(commitJson);
            }
            catch (Exception ex)
            {
                Debug.LogError("Error sending audio commit event: " + ex.Message);
            }
        }

        private void Update()
        {
            // Always dispatch the message queue so that events are processed.
            if (ws != null)
            {
                ws.DispatchMessageQueue();
            }
        }

    }
}
