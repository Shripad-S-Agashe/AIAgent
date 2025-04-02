using NativeWebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace OpenAI
{
    /// <summary>
    /// This class handles speech-to-speech functionality by creating a realtime session,
    /// connecting via WebSocket, and sending audio events (append + commit) based on
    /// user microphone input. The microphone is selected from a dropdown at startup.
    /// Recording starts when the record button is pressed and stops after a fixed duration.
    /// WebSocket responses (audio bytes) are forwarded to the OpenAIMessageHandler.
    /// </summary>
    public class SpeechToSpeech : MonoBehaviour
    {
        public string SessionSecret { get; private set; }

        private OpenAIApi2 openAIApi;
        private WebSocket ws;

        [Header("UI Elements")]
        [SerializeField] private Button recordButton;
        [SerializeField] private Dropdown micDropdown;
        [SerializeField] private Image progressBar;
        [SerializeField] private Text message; // For logging output

        [Header("Audio Playback")]
        [SerializeField] public OpenAIMessageHandler messageHandler; // Responsible for playing audio

        // Audio parameters.
        private string micName;
        private int sampleRate = 16000; // Adjust as needed.
        private AudioClip recordingClip;
        private bool isRecording = false;
        private float recordingTime = 0f;
        private int maxRecordDuration = 5; // Duration in seconds; adjust as desired.

        private async void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            micDropdown.options.Add(new Dropdown.OptionData("Microphone not supported on WebGL"));
#else
            // Populate the mic dropdown with available devices.
            foreach (var device in Microphone.devices)
            {
                micDropdown.options.Add(new Dropdown.OptionData(device));
            }
            micDropdown.onValueChanged.AddListener(ChangeMicrophone);

            // Retrieve the last selected mic index (default to 0 if not set).
            int index = PlayerPrefs.GetInt("user-mic-device-index", 0);
            micDropdown.SetValueWithoutNotify(index);
            micName = micDropdown.options[index].text;
            LogOutput("Using microphone: " + micName);
#endif
            // Set up the record button.
            recordButton.onClick.AddListener(OnRecordButtonPressed);

            // Now initialize the realtime session and WebSocket connection.
            openAIApi = new OpenAIApi2();
            await InitializeSessionAsync();
            await ConnectToWebSocketAsync();
        }

        /// <summary>
        /// Called when the microphone dropdown value is changed.
        /// </summary>
        /// <param name="index">The selected microphone index.</param>
        private void ChangeMicrophone(int index)
        {
            PlayerPrefs.SetInt("user-mic-device-index", index);
            micName = micDropdown.options[index].text;
            LogOutput("Selected microphone: " + micName);
        }

        /// <summary>
        /// Initializes the realtime session and retrieves the session secret.
        /// </summary>
        public async Task InitializeSessionAsync()
        {
            LogOutput("Creating OpenAI Realtime Session...");

            var request = new OpenAIApi2.RealtimeSessionRequest
            {
                model = "gpt-4o-realtime-preview",
                instructions = "You are a friendly assistant.",
                modalities = new string[] { "audio", "text" }
            };

            var response = await openAIApi.CreateRealtimeSession(request);
            if (response?.client_secret != null)
            {
                SessionSecret = response.client_secret.value;
                LogOutput("Session Secret: " + SessionSecret);
            }
            else
            {
                LogOutput("Failed to create realtime session or retrieve session secret.");
            }
        }

        /// <summary>
        /// Connects to the realtime WebSocket using the session secret.
        /// </summary>
        public async Task ConnectToWebSocketAsync()
        {
            LogOutput("Connecting to OpenAI realtime WebSocket...");

            ws = await openAIApi.ConnectRealtimeWebSocket(
                ephemeralToken: SessionSecret,
                onOpenCallback: (wsInstance) =>
                {
                    LogOutput("Connected to server.");
                    ws = wsInstance;
                },
                onMessageCallback: (bytes) =>
                    {
                        // Simply forward the received bytes to our dedicated handler method.
                        HandleMessageCallback(bytes);
                    },
                onErrorCallback: (errorMsg) =>
                {
                    LogOutput("WebSocket Error: " + errorMsg);
                },
                onCloseCallback: (closeCode) =>
                {
                    LogOutput("WebSocket closed with code: " + closeCode);
                },
                model: "gpt-4o-realtime-preview-2024-12-17" // Adjust the model if needed.
            );
        }

        private void Update()
        {
            ws?.DispatchMessageQueue();

            // Update recording progress.
            if (isRecording)
            {
                recordingTime += Time.deltaTime;
                progressBar.fillAmount = recordingTime / maxRecordDuration;
            }
        }

        /// <summary>
        /// Called when the record button is pressed.
        /// Starts the recording and schedules the stop after the maximum duration.
        /// </summary>
        private void OnRecordButtonPressed()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LogOutput("Recording not supported on WebGL.");
#else
            if (!isRecording)
            {
                StartRecording();
                recordButton.interactable = false;
                // Schedule stop recording after maxRecordDuration seconds.
                Invoke(nameof(StopRecordingAndSendAudio), maxRecordDuration);
            }
#endif
        }

        /// <summary>
        /// Starts recording audio from the selected microphone.
        /// </summary>
        private void StartRecording()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LogOutput("Recording not supported on WebGL.");
            return;
#else
            if (string.IsNullOrEmpty(micName))
            {
                LogOutput("No microphone selected.");
                return;
            }
            LogOutput("Starting recording with mic: " + micName);
            recordingTime = 0f;
            progressBar.fillAmount = 0f;
            recordingClip = Microphone.Start(micName, false, maxRecordDuration, sampleRate);
            isRecording = true;
#endif
        }

        /// <summary>
        /// Stops recording, processes the audio, and sends the audio events (append then commit) to the server.
        /// </summary>
        private async void StopRecordingAndSendAudio()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LogOutput("Recording not supported on WebGL.");
            return;
#else
            if (!isRecording)
                return;

            LogOutput("Stopping recording...");
            int recordedSamples = Microphone.GetPosition(micName);
            Microphone.End(micName);
            isRecording = false;
            LogOutput("Recording stopped. Recorded samples: " + recordedSamples);

            if (recordedSamples <= 0)
            {
                LogOutput("No audio recorded.");
                recordButton.interactable = true;
                return;
            }

            // Retrieve the recorded audio samples.
            float[] samples = new float[recordedSamples * recordingClip.channels];
            recordingClip.GetData(samples, 0);

            // Convert float samples (-1 to 1) to 16-bit PCM bytes.
            byte[] pcmBytes = ConvertToPCM16(samples);
            LogOutput("Audio converted to PCM16 with " + pcmBytes.Length + " bytes.");

            // Encode PCM data to Base64.
            string base64Audio = Convert.ToBase64String(pcmBytes);

            // Create and send the append event.
            var appendPayload = new Dictionary<string, object>
            {
                { "event_id", "event_" + Guid.NewGuid().ToString("N") },
                { "type", "input_audio_buffer.append" },
                { "audio", base64Audio }
            };

            string appendJson = JsonConvert.SerializeObject(appendPayload);
            LogOutput("Sending mic audio append event. Payload length: " + appendJson.Length);

            if (ws == null)
            {
                LogOutput("WebSocket instance is null. Cannot send message.");
                recordButton.interactable = true;
                return;
            }

            try
            {
                await ws.SendText(appendJson);
            }
            catch (Exception ex)
            {
                LogOutput("Error sending mic audio append event: " + ex.Message);
            }

            // Wait briefly before sending the commit event.
            await Task.Delay(500);
            await SendAudioCommitMessage();

            recordButton.interactable = true;
#endif
        }

        /// <summary>
        /// Sends an input_audio_buffer.commit event to prompt the server to process the appended audio.
        /// </summary>
        private async Task SendAudioCommitMessage()
        {
            var commitPayload = new Dictionary<string, object>
            {
                { "event_id", "event_" + Guid.NewGuid().ToString("N") },
                { "type", "input_audio_buffer.commit" }
            };

            string commitJson = JsonConvert.SerializeObject(commitPayload);
            LogOutput("Sending audio buffer commit event: " + commitJson);

            try
            {
                await ws.SendText(commitJson);
            }
            catch (Exception ex)
            {
                LogOutput("Error sending audio commit event: " + ex.Message);
            }
        }

        /// <summary>
        /// Converts an array of float audio samples (range -1 to 1) into a 16-bit PCM (little-endian) byte array.
        /// </summary>
        private byte[] ConvertToPCM16(float[] samples)
        {
            byte[] bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short intSample = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                byte[] sampleBytes = BitConverter.GetBytes(intSample);
                bytes[i * 2] = sampleBytes[0];
                bytes[i * 2 + 1] = sampleBytes[1];
            }
            return bytes;
        }

        /// <summary>
        /// Appends a message to the UI text output and logs it.
        /// </summary>
        private void LogOutput(string msg)
        {
            if (message != null)
            {
                message.text += msg + "\n";
            }
            Debug.Log(msg);
        }


        /// <summary>
        /// Handles incoming messages by checking if the type is a "response.audio.delta".
        /// If so, it extracts the audio delta, decodes it from Base64, and calls the external message handler's PlayAudio method.
        /// Otherwise, it forwards the raw bytes to HandleWebSocketMessage.
        /// </summary>
        private void HandleMessageCallback(byte[] bytes)
        {
            string msg = Encoding.UTF8.GetString(bytes);
            try
            {
                var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(msg);
                if (json != null && json.TryGetValue("type", out object typeObj))
                {
                    string type = typeObj.ToString();
                    if (type == "response.audio.delta")
                    {
                        if (json.TryGetValue("delta", out object deltaObj))
                        {
                            string deltaBase64 = deltaObj.ToString();
                            byte[] deltaBytes = Convert.FromBase64String(deltaBase64);
                            if (messageHandler != null)
                            {
                                messageHandler.PlayAudio(deltaBytes);
                            }
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Error parsing JSON message: " + ex.Message);
            }
            // For any other message types, pass to the default handler if desired.
           // messageHandler?.HandleWebSocketMessage(bytes);
        }

    }
}
