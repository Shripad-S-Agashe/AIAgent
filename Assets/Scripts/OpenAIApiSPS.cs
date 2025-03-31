using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace OpenAI
{
    // Request class for realtime speech-to-speech.
    public class CreateSpeechToSpeechRequest
    {
        // Always enable streaming for realtime.
        public bool Stream { get; set; } = true;

        // Path to an audio file or provide audio as byte data.
        public string File { get; set; }
        public byte[] FileData { get; set; }

        // Specify the realtime model to use.
        public string Model { get; set; }

        // Include additional parameters if required by the realtime API
        // e.g., language, context, etc.
    }

    // Response class for realtime speech-to-speech.
    public class CreateSpeechToSpeechResponse : IResponse
    {
        [JsonProperty("audio_chunk")]
        public string AudioChunk { get; set; }

        [JsonProperty("warning")]
        public string Warning { get; set; }
        [JsonProperty("error")]
        public ApiError Error { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }


    }
}

namespace OpenAI
{
    public class OpenAIApiSPS : OpenAIApi
    {
        private const string BASE_PATH = "https://api.openai.com/v1";
        private readonly string _apiKey;
        private readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver
            {
                // Convert property names to snake_case.
                NamingStrategy = new SnakeCaseNamingStrategy()
            }
        };

        public OpenAIApiSPS(string apiKey)
        {
            _apiKey = apiKey;
        }

        /// <summary>
        /// Creates a JSON payload from the given request object.
        /// </summary>
        private byte[] CreatePayload<T>(T request)
        {
            var json = JsonConvert.SerializeObject(request, jsonSerializerSettings);
            return Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// Sends an audio file to the realtime speech-to-speech endpoint and streams back audio chunks.
        /// </summary>
        /// <param name="request">A realtime speech-to-speech request object.</param>
        /// <param name="onAudioChunk">Callback to handle incoming audio chunk (as byte array).</param>
        /// <param name="onComplete">Callback invoked once the stream is complete.</param>
        /// <param name="token">Cancellation token source.</param>
        public void CreateSpeechToSpeechAsync(
            CreateSpeechToSpeechRequest request,
            Action<byte[]> onAudioChunk,
            Action onComplete,
            CancellationTokenSource token)
        {
            var path = $"{BASE_PATH}/voice/speechtospeech";
            var payload = CreatePayload(request);
            // Fire-and-forget the asynchronous streaming request.
            _ = DispatchSpeechToSpeechRequest(path, HttpMethod.Post, onAudioChunk, onComplete, token, payload);
        }

        /// <summary>
        /// A streaming dispatcher that processes realtime responses, decodes audio chunks, and invokes callbacks.
        /// </summary>
        private async Task DispatchSpeechToSpeechRequest(
            string path,
            HttpMethod method,
            Action<byte[]> onAudioChunk,
            Action onComplete,
            CancellationTokenSource token,
            byte[] payload = null)
        {
            using (var client = new HttpClient())
            {
                // Prepare the HTTP request.
                var requestMessage = new HttpRequestMessage(method, path)
                {
                    Content = new ByteArrayContent(payload)
                };
                requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                // Send the request and enable response streaming.
                using (var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, token.Token))
                {
                    response.EnsureSuccessStatusCode();
                    // Note: ReadAsStreamAsync does not support a cancellation token in netstandard 2.1.
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var reader = new StreamReader(stream))
                    {
                        while (!reader.EndOfStream && !token.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync();
                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            // Check for end-of-stream signal.
                            if (line.Contains("[DONE]"))
                            {
                                onComplete?.Invoke();
                                return;
                            }

                            // Remove any SSE prefix, for example "data: ".
                            string cleanLine = line.Replace("data: ", "").Trim();

                            try
                            {
                                var responseChunk = JsonConvert.DeserializeObject<CreateSpeechToSpeechResponse>(cleanLine, jsonSerializerSettings);
                                if (responseChunk?.Error != null)
                                {
                                    Console.Error.WriteLine($"Error: {responseChunk.Error.Message}");
                                    continue;
                                }
                                if (!string.IsNullOrEmpty(responseChunk?.AudioChunk))
                                {
                                    byte[] audioData = Convert.FromBase64String(responseChunk.AudioChunk);
                                    onAudioChunk?.Invoke(audioData);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"Failed to parse chunk: {ex.Message}");
                            }
                        }
                        onComplete?.Invoke();
                    }
                }
            }
        }
    }
}
