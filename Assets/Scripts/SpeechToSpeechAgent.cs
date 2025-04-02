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
    /// This class now streams microphone audio continuously by reading new samples 
    /// periodically and sending input_audio_buffer.append events. It does not rely on a fixed duration,
    /// and it doesn't send a commit event since server-side VAD is enabled.
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
        private int sampleRate = 16000;
        private AudioClip recordingClip;
        private bool isRecording = false;
        private int lastSamplePosition = 0;
        // Using a long duration to enable continuous recording.
        private int recordingDuration = 300; // seconds

        // Adjust how frequently (in seconds) new audio is sent.
        private float sendInterval = 0.25f;

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
                    // Forward the received bytes to our dedicated handler.
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
                model: "gpt-4o-realtime-preview-2024-12-17"
            );
        }

        private void Update()
        {
            ws?.DispatchMessageQueue();
        }

        /// <summary>
        /// Toggles recording on/off when the record button is pressed.
        /// </summary>
        private void OnRecordButtonPressed()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LogOutput("Recording not supported on WebGL.");
#else
            if (!isRecording)
            {
                StartRecording();
                recordButton.GetComponentInChildren<Text>().text = "Stop Recording";
            }
            else
            {
                StopRecording();
                recordButton.GetComponentInChildren<Text>().text = "Record";
            }
#endif
        }

        /// <summary>
        /// Starts recording from the selected microphone and begins streaming audio.
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
            LogOutput("Starting continuous recording with mic: " + micName);
            // Start a looping recording with a long duration.
            recordingClip = Microphone.Start(micName, true, recordingDuration, sampleRate);
            lastSamplePosition = 0;
            isRecording = true;
            // Start a coroutine to stream audio chunks.
            StartCoroutine(StreamAudioCoroutine());
#endif
        }

        /// <summary>
        /// Stops recording.
        /// </summary>
        private void StopRecording()
        {
            if (!isRecording)
                return;

            LogOutput("Stopping recording...");
            Microphone.End(micName);
            isRecording = false;
        }

        /// <summary>
        /// Coroutine that continuously reads new audio data from the recording clip and sends it to the server.
        /// </summary>
        private System.Collections.IEnumerator StreamAudioCoroutine()
        {
            while (isRecording)
            {
                int currentPosition = Microphone.GetPosition(micName);
                int sampleCount = 0;
                float[] samples = null;

                // Handle the case when the recording wraps around.
                if (currentPosition < lastSamplePosition)
                {
                    // Calculate samples from lastSamplePosition to end of clip.
                    sampleCount = recordingClip.samples - lastSamplePosition;
                    samples = new float[sampleCount];
                    recordingClip.GetData(samples, lastSamplePosition);

                    // Process and send the first half.
                    if (sampleCount > 0)
                    {
                        SendAudioChunk(samples);
                    }

                    // Then read from beginning to currentPosition.
                    if (currentPosition > 0)
                    {
                        sampleCount = currentPosition;
                        samples = new float[sampleCount];
                        recordingClip.GetData(samples, 0);
                        SendAudioChunk(samples);
                    }
                }
                else
                {
                    sampleCount = currentPosition - lastSamplePosition;
                    if (sampleCount > 0)
                    {
                        samples = new float[sampleCount];
                        recordingClip.GetData(samples, lastSamplePosition);
                        SendAudioChunk(samples);
                    }
                }

                lastSamplePosition = currentPosition;

                yield return new WaitForSeconds(sendInterval);
            }
        }

        /// <summary>
        /// Converts float samples to 16-bit PCM, encodes them as Base64, and sends an append event.
        /// </summary>
        private async void SendAudioChunk(float[] samples)
        {
            if (samples == null || samples.Length == 0 || ws == null)
                return;

            byte[] pcmBytes = ConvertToPCM16(samples);
            LogOutput("Sending audio chunk: " + pcmBytes.Length + " bytes.");

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

            try
            {
                await ws.SendText(appendJson);
            }
            catch (Exception ex)
            {
                LogOutput("Error sending audio chunk: " + ex.Message);
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
                message.text = msg;
            }
            Debug.Log(msg);
        }

        /// <summary>
        /// Handles incoming messages by checking for different types of events.
        /// If the type is "response.audio.delta", it extracts the audio delta and plays it.
        /// If the type is "response.content_part.done", it displays the transcript.
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
                    else if (type == "response.content_part.done")
                    {
                        // When a content part is done, display the transcript from the response.
                        DisplayTranscript(json);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Error parsing JSON message: " + ex.Message);
            }
        }

        /// <summary>
        /// Extracts the transcript from the JSON payload and prints it to the UI.
        /// </summary>
        private void DisplayTranscript(Dictionary<string, object> json)
        {
            if (json.TryGetValue("part", out object partObj))
            {
                var partJObject = partObj as Newtonsoft.Json.Linq.JObject;
                if (partJObject != null && partJObject.TryGetValue("transcript", out var transcriptToken))
                {
                    string transcript = transcriptToken.ToString();
                    LogOutput("Transcript: " + transcript);
                    return;
                }
            }
            LogOutput("Transcript not found in the response.");
        }
    }
}
