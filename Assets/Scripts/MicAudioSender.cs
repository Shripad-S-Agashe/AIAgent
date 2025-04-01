using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using NativeWebSocket;

public class MicAudioSender : MonoBehaviour
{
    public int sampleRate = 16000;
    public float chunkDurationSeconds = 0.2f; // 200ms chunks
    private int chunkSize;
    private AudioClip micClip;
    private string micDevice;
    private int lastSamplePos = 0;
    private bool isRecording = false;

    public WebSocket ws;

    void Start()
    {
        chunkSize = Mathf.CeilToInt(sampleRate * chunkDurationSeconds);
        micDevice = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;
        if (string.IsNullOrEmpty(micDevice))
        {
            Debug.LogError("No microphone device found!");
            return;
        }
    }

    public void StartRecording()
    {
        if (isRecording) return;

        micClip = Microphone.Start(micDevice, true, 10, sampleRate);
        isRecording = true;
        StartCoroutine(SendMicData());
    }

    public void StopRecording()
    {
        if (!isRecording) return;

        isRecording = false;
        Microphone.End(micDevice);

        // Send end_of_audio marker
        string endJson = "{\"type\":\"end_of_audio\"}";
        byte[] endBytes = System.Text.Encoding.UTF8.GetBytes(endJson);
        ws.Send(endBytes);
    }

    private IEnumerator SendMicData()
    {
        while (isRecording)
        {
            int currentPos = Microphone.GetPosition(micDevice);
            int samplesToSend = (currentPos - lastSamplePos + micClip.samples) % micClip.samples;

            if (samplesToSend >= chunkSize)
            {
                float[] samples = new float[chunkSize];
                micClip.GetData(samples, lastSamplePos);
                byte[] pcmBytes = ConvertToPCM16(samples);

                ws.Send(pcmBytes); // Send raw bytes

                lastSamplePos = (lastSamplePos + chunkSize) % micClip.samples;
            }

            yield return null;
        }
    }

    private byte[] ConvertToPCM16(float[] samples)
    {
        short[] intData = new short[samples.Length];
        byte[] bytesData = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            intData[i] = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            byte[] byteArr = BitConverter.GetBytes(intData[i]);
            bytesData[i * 2] = byteArr[0];
            bytesData[i * 2 + 1] = byteArr[1];
        }
        return bytesData;
    }
}
