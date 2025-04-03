using NativeWebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using static OpenAI.OpenAIApi2;

namespace OpenAI
{
    /// <summary>
    /// This class streams microphone audio continuously by reading new samples 
    /// periodically and sending input_audio_buffer.append events. It does not rely on a fixed duration,
    /// and it doesn't send a commit event since server-side VAD is enabled.
    /// </summary>
    public class SpeechToSpeech : MonoBehaviour
    {
        public string SessionSecret { get; private set; }

        private OpenAIApi2 openAIApi;
        private WebSocket ws;

        [Header("UI Elements")]
        [SerializeField] private Button StartTalkingButton;
        [SerializeField] private Button PromptInAction;
        [SerializeField] private Button PromptOnAction;
        [SerializeField] private Dropdown micDropdown;
        [SerializeField] private Image progressBar;
        [SerializeField] private Text message; // For transcript display only

        [Header("Audio Playback")]
        [SerializeField] public OpenAIMessageHandler messageHandler; // Responsible for playing audio

        // Audio parameters.
        private string micName;
        private readonly int sampleRate = 16000;
        private AudioClip recordingClip;
        private bool isRecording = false;
        private int lastSamplePosition = 0;
        // Using a long duration to enable continuous recording.
        private readonly int recordingDuration = 300; // seconds

        // Adjust how frequently (in seconds) new audio is sent.
        private readonly float sendInterval = 0.25f;

        [Header("AI Configuration")]
        [SerializeField] private AIVoices AIVoice = AIVoices.coral; // Maximum recording duration in seconds
        [SerializeField] private string model = "gpt-4o-realtime-preview"; // Default model, can be changed if needed


        // Add these in your SpeechToSpeech class
        [SerializeField] private string GenralInstructions = "You are a very crank onld lady, dont be helpfull but mean you are suppose to question the user";
        [SerializeField] private string CommonPromptInstructions = "Common Stuff for prompts"; 
        [SerializeField] private string InActionSystemPrompt = "Please provide an in-action reflection to help the player reassess their approach.";
        [SerializeField] private string OnActionSystemPrompt = "Please provide an on-action reflection to help the player understand what worked and what to improve next time.";

        private string systemPromptInAction = "System Update (In-Action): [Player is struggling with a puzzle]";
        private string systemPromptOnAction = "System Update (On-Action): [Player has completed a a puzzle]";


        private async void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            micDropdown.options.Add(new Dropdown.OptionData("Microphone not supported on WebGL"));
#else
            // Populate the mic dropdown with available devices.
            foreach (string device in Microphone.devices)
            {
                micDropdown.options.Add(new Dropdown.OptionData(device));
            }
            micDropdown.onValueChanged.AddListener(ChangeMicrophone);


            // Retrieve the last selected mic index (default to 0 if not set).
            int index = PlayerPrefs.GetInt("user-mic-device-index", 0);
            micDropdown.SetValueWithoutNotify(index);
            micName = micDropdown.options[index].text;
            Debug.Log("Using microphone: " + micName);
#endif
            // Set up the record button.
            StartTalkingButton.onClick.AddListener(OnRecordButtonPressed);
            // For In-Action Reflection:
            PromptInAction.onClick.AddListener(async () =>
            {
                await SendSystemMessage(systemPromptInAction);
                // Use an instruction string for in-action reflection – adjust as needed.
                await SendResponseCreate(systemPromptInAction + GenralInstructions + CommonPromptInstructions + InActionSystemPrompt);
            });

            // For On-Action Reflection:
            PromptOnAction.onClick.AddListener(async () =>
            {
                await SendSystemMessage(systemPromptOnAction);
                // Use an instruction string for on-action reflection – adjust as needed.
                await SendResponseCreate(OnActionSystemPrompt + GenralInstructions + CommonPromptInstructions + OnActionSystemPrompt);
            });


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
            Debug.Log("Selected microphone: " + micName);
        }

        /// <summary>
        /// Initializes the realtime session and retrieves the session secret.
        /// </summary>
        public async Task InitializeSessionAsync()
        {
            Debug.Log("Creating OpenAI Realtime Session...");

            RealtimeSessionRequest request = new()
            {
                model = model,
                instructions = GenralInstructions,
                modalities = new string[] { "audio", "text" },
                voice = AIVoice.ToString().ToLowerInvariant(),
            };

            RealtimeSessionResponse response = await openAIApi.CreateRealtimeSession(request);
            if (response?.client_secret != null)
            {
                SessionSecret = response.client_secret.value;
                Debug.Log("Session Secret: " + SessionSecret);
            }
            else
            {
                Debug.Log("Failed to create realtime session or retrieve session secret.");
            }
        }

        /// <summary>
        /// Connects to the realtime WebSocket using the session secret.
        /// </summary>
        public async Task ConnectToWebSocketAsync()
        {
            Debug.Log("Connecting to OpenAI realtime WebSocket...");

            ws = await openAIApi.ConnectRealtimeWebSocket(
                ephemeralToken: SessionSecret,
                onOpenCallback: (wsInstance) =>
                {
                    Debug.Log("Connected to server.");
                    ws = wsInstance;
                },
                onMessageCallback: (bytes) =>
                {
                    // Forward the received bytes to our dedicated handler.
                    HandleMessageCallback(bytes);
                },
                onErrorCallback: (errorMsg) =>
                {
                    Debug.Log("WebSocket Error: " + errorMsg);
                },
                onCloseCallback: (closeCode) =>
                {
                    Debug.Log("WebSocket closed with code: " + closeCode);
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
            Debug.Log("Recording not supported on WebGL.");
#else
            if (!isRecording)
            {
                StartRecording();
                StartTalkingButton.GetComponentInChildren<Text>().text = "Stop Recording";
            }
            else
            {
                StopRecording();
                StartTalkingButton.GetComponentInChildren<Text>().text = "Record";
            }
#endif
        }

        /// <summary>
        /// Starts recording from the selected microphone and begins streaming audio.
        /// </summary>
        private void StartRecording()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("Recording not supported on WebGL.");
            return;
#else
            if (string.IsNullOrEmpty(micName))
            {
                Debug.Log("No microphone selected.");
                return;
            }
            Debug.Log("Starting continuous recording with mic: " + micName);
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
            {
                return;
            }

            Debug.Log("Stopping recording...");
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
                int sampleCount;
                float[] samples;
                // Handle the case when the recording wraps around.
                if (currentPosition < lastSamplePosition)
                {
                    // Calculate samples from lastSamplePosition to end of clip.
                    sampleCount = recordingClip.samples - lastSamplePosition;
                    samples = new float[sampleCount];
                    _ = recordingClip.GetData(samples, lastSamplePosition);

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
                        _ = recordingClip.GetData(samples, 0);
                        SendAudioChunk(samples);
                    }
                }
                else
                {
                    sampleCount = currentPosition - lastSamplePosition;
                    if (sampleCount > 0)
                    {
                        samples = new float[sampleCount];
                        _ = recordingClip.GetData(samples, lastSamplePosition);
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
            {
                return;
            }

            byte[] pcmBytes = ConvertToPCM16(samples);

            // Encode PCM data to Base64.
            string base64Audio = Convert.ToBase64String(pcmBytes);

            // Create and send the append event.
            Dictionary<string, object> appendPayload = new()
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
                Debug.Log("Error sending audio chunk: " + ex.Message);
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
                bytes[(i * 2) + 1] = sampleBytes[1];
            }
            return bytes;
        }

        /// <summary>
        /// Displays transcript messages on the UI.
        /// </summary>
        private void LogOutput(string msg)
        {
            // Only update the UI with transcript messages.
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
                Dictionary<string, object> json = JsonConvert.DeserializeObject<Dictionary<string, object>>(msg);
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
                if (partObj is Newtonsoft.Json.Linq.JObject partJObject && partJObject.TryGetValue("transcript", out Newtonsoft.Json.Linq.JToken transcriptToken))
                {
                    string transcript = transcriptToken.ToString();
                    LogOutput("Transcript: " + transcript);
                    return;
                }
            }
            LogOutput("Transcript not found in the response.");
        }


        private async Task SendSystemMessage(string text)
        {
            Dictionary<string, object> systemPayload = new()
            {
                { "event_id", "event_" + Guid.NewGuid().ToString("N") },
                { "type", "conversation.item.create" },
                { "previous_item_id", null }, // Optional: You can track message chaining if needed
                { "item", new Dictionary<string, object>
                    {
                        { "type", "message" },
                        { "role", "system" }, // Or "system" if it's a neutral prompt
                        { "content", new List<Dictionary<string, object>>
                            {
                                new() {
                                    { "type", "input_text" },
                                    { "text", text }
                                }
                            }
                        }
                    }
                }
            };

            string systemJson = JsonConvert.SerializeObject(systemPayload);
            try
            {
                await ws.SendText(systemJson);
            }
            catch (Exception ex)
            {
                Debug.Log("Error sending system message: " + ex.Message);
            }
        }


        // This method sends a "response.create" event to prompt the agent to produce a voice response.
       // The instructions parameter can be customized for In-Action or On-Action reflection.
        private async Task SendResponseCreate(string instructions)
        {
            var responsePayload = new Dictionary<string, object>
        {
        { "event_id", "event_" + Guid.NewGuid().ToString("N") },
        { "type", "response.create" },
        { "response", new Dictionary<string, object>
            {
                { "modalities", new string[] { "text", "audio" } },
                { "instructions", instructions },
                { "output_audio_format", "pcm16" },
            }
        }
        };
        string responseJson = JsonConvert.SerializeObject(responsePayload);
            try
            {
                await ws.SendText(responseJson);
            }
            catch (Exception ex)
            {
                Debug.Log("Error sending response.create event: " + ex.Message);
            }
        }

    }
}
