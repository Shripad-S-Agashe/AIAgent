using UnityEngine;
using System.Collections.Generic;

public class OpenAIMessageHandler : MonoBehaviour
{
    public AudioSource audioSource;
    private Queue<float> audioBuffer = new Queue<float>();
    private const int sampleRate = 16000;
    private const int bufferLengthSeconds = 10; // 10 seconds buffer length
    private AudioClip streamingClip;
    private int playbackPosition = 0;

    private void Start()
    {
        streamingClip = AudioClip.Create("StreamingAudio", sampleRate * bufferLengthSeconds, 1, sampleRate, true, OnAudioRead);
        audioSource.clip = streamingClip;
        audioSource.loop = true;
        audioSource.Play();
    }

    public void PlayAudio(byte[] newAudioBytes)
    {
        int sampleCount = newAudioBytes.Length / 2;
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = System.BitConverter.ToInt16(newAudioBytes, i * 2);
            audioBuffer.Enqueue(sample / 32768f);
        }
        // If the AudioSource is stopped (e.g., after an interruption), resume playback.
        if (!audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }


    private void OnAudioRead(float[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (audioBuffer.Count > 0)
                data[i] = audioBuffer.Dequeue();
            else
                data[i] = 0; // Silence if no data available
        }
    }

    public void UserTalking()
    {
        // Stop playback if active
        if (audioSource.isPlaying)
        {
            audioSource.Stop();
        }
        else 
        {
            return;
        }

        // Clear any queued audio data
        audioBuffer.Clear();

        // Reset the playback position if necessary
        playbackPosition = 0;

        Debug.Log("Audio playback stopped and buffer cleared due to user interruption.");
    }

}
