using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using OpenAI;
using System.Threading.Tasks;
using Samples.Whisper;

public class SpeechToSpeech : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button recordButton;
    [SerializeField] private Text debugText; // For debugging/transcription display

    [Header("Recording Settings")]
    [SerializeField] private float recordDuration = 5f; // Seconds to record

    private AudioClip clip;
    private OpenAIApi openai = new OpenAIApi();

    private void Start()
    {
        // Add listener to the record button
        recordButton.onClick.AddListener(StartRecording);
    }

    // Begin recording from the default microphone
    private void StartRecording()
    {
        recordButton.interactable = false;
        debugText.text = "Recording...";
        clip = Microphone.Start(null, false, (int)recordDuration, 44100);
        StartCoroutine(StopRecordingAfter(recordDuration));
    }

    // Stop recording after the specified duration
    private IEnumerator StopRecordingAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        StopRecording();
    }

    // Stop the microphone and start processing the recorded audio
    private async void StopRecording()
    {
        Microphone.End(null);
        debugText.text = "Processing audio...";

        // Convert the AudioClip to WAV byte data (using your SaveWav utility)
        byte[] audioData = SaveWav.Save("output.wav", clip);

        // Transcribe the audio using Whisper
        var transcriptionRequest = new CreateAudioTranscriptionsRequest
        {
            FileData = new FileData { Data = audioData, Name = "audio.wav" },
            Model = "whisper-1",
            Language = "en"
        };

        var transcriptionResponse = await openai.CreateAudioTranscription(transcriptionRequest);
        string userSpeech = transcriptionResponse.Text;
        debugText.text = "You said: " + userSpeech;

        // Use the transcription as input for a chat completion
        var chatMessages = new List<ChatMessage>
        {
            new ChatMessage { Role = "user", Content = userSpeech }
        };
        var chatRequest = new CreateChatCompletionRequest
        {
            Model = "gpt-4o-mini", // Replace with your desired model
            Messages = chatMessages
        };

        var chatResponse = await openai.CreateChatCompletion(chatRequest);
        string botResponse = chatResponse.Choices[0].Message.Content.Trim();
        debugText.text += "\nBot: " + botResponse;

        // Convert the bot response to speech
        Speak(botResponse);

        // Re-enable the record button for another round
        recordButton.interactable = true;
    }

    // Placeholder for text-to-speech functionality.
    // Replace this with your preferred TTS solution (plugin, API, etc.)
    private void Speak(string text)
    {
        Debug.Log("Speaking: " + text);
        // Example: if you integrate a TTS plugin, you might call:
        // TTSManager.Instance.Speak(text);
    }
}
