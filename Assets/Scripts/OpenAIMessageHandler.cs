using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class OpenAIMessageHandler : MonoBehaviour
{
    [Tooltip("Assign an AudioSource component that will play the received audio.")]
    public AudioSource audioSource;

    // A buffer to accumulate incoming PCM16 audio bytes.
    private List<byte> audioQueue = new List<byte>();
    private bool isPlayingQueue = false;

    /// <summary>
    /// Call this method to add new audio bytes to the queue.
    /// </summary>
    /// <param name="newAudioBytes">New PCM16 audio bytes (16kHz mono) received from the delta response.</param>
    public void PlayAudio(byte[] newAudioBytes)
    {
        // Append the new bytes to the queue.
        audioQueue.AddRange(newAudioBytes);
        Debug.Log("Queued " + newAudioBytes.Length + " bytes; total in queue: " + audioQueue.Count);

        // If nothing is playing, start the coroutine to play queued audio.
        if (!isPlayingQueue)
        {
            StartCoroutine(PlayAudioFromQueue());
        }
    }

    /// <summary>
    /// Coroutine that checks the audio queue and plays audio sequentially.
    /// </summary>
    private IEnumerator PlayAudioFromQueue()
    {
        isPlayingQueue = true;

        // Define a minimum number of bytes to play (for example, 100ms of audio at 16kHz, 16-bit mono is ~3200 bytes)
        int minimumBytesToPlay = 3200;

        while (audioQueue.Count > 0)
        {
            // Wait until we have at least the minimum amount of data.
            if (audioQueue.Count < minimumBytesToPlay)
            {
                // Optionally, wait a short time to allow more audio to accumulate.
                yield return new WaitForSeconds(0.05f);
                continue;
            }

            // Copy the accumulated bytes to a new array and clear the queue.
            byte[] chunk = audioQueue.ToArray();
            audioQueue.Clear();

            // Convert PCM16 bytes to float samples.
            int sampleCount = chunk.Length / 2;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(chunk, i * 2);
                samples[i] = sample / 32768f;
            }

            // Create an AudioClip from the samples.
            AudioClip clip = AudioClip.Create("ResponseAudio", sampleCount, 1, 16000, false);
            clip.SetData(samples, 0);

            // Play the clip.
            audioSource.clip = clip;
            audioSource.Play();
            Debug.Log("Playing audio clip with " + sampleCount + " samples.");

            // Wait until the clip has finished playing.
            yield return new WaitUntil(() => !audioSource.isPlaying);
        }

        isPlayingQueue = false;
    }

    /// <summary>
    /// (Optional) Handles non-delta messages or other types of incoming data.
    /// </summary>
    public void HandleWebSocketMessage(byte[] bytes)
    {
        Debug.Log("Received non-delta message (" + bytes.Length + " bytes).");
        // Additional handling can be implemented here.
    }
}
