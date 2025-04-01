using UnityEngine;
using System;
using System.Text;
using System.Collections.Generic;

public class OpenAIMessageHandler : MonoBehaviour
{
    public AudioSource audioSource;

    private List<byte> audioBuffer = new List<byte>();

    public void HandleWebSocketMessage(byte[] bytes)
    {
        // Try to parse as UTF-8 text first
        string msg = Encoding.UTF8.GetString(bytes);

        if (msg.StartsWith("{"))
        {
            Debug.Log("Received JSON message: " + msg);
            // You can parse and handle types like "transcript" or "text" here
            return;
        }

        // Otherwise, assume it’s audio bytes (raw PCM 16kHz mono)
        audioBuffer.AddRange(bytes);

        // Optional: once buffer is big enough, play it
        if (audioBuffer.Count > 32000) // ~1s audio
        {
            PlayBufferedAudio();
            audioBuffer.Clear();
        }
    }

    private void PlayBufferedAudio()
    {
        int sampleCount = audioBuffer.Count / 2;
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(audioBuffer.ToArray(), i * 2);
            samples[i] = sample / 32768f;
        }

        AudioClip clip = AudioClip.Create("ResponseAudio", sampleCount, 1, 16000, false);
        clip.SetData(samples, 0);
        audioSource.clip = clip;
        audioSource.Play();
    }
}
