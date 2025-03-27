using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

[CreateAssetMenu(fileName = "AIAgentSettings", menuName = "AI Agent/Settings", order = 1)]
public class AIAgentSettings : ScriptableObject
{
    [Header("API Configuration")]
    [Tooltip("Your OpenAI API key. If not set, it will be loaded from %USERPROFILE%/.openai/auth.json")]
    private string apiKey;

    [Tooltip("Organization ID (if applicable).")]
    private string organization;

    [Tooltip("Base URL for the OpenAI API.")]
    public string baseUrl = "https://api.openai.com/v1";

    /// <summary>
    /// Loads credentials from the auth.json file located in %USERPROFILE%/.openai/
    /// Expected JSON format: { "apiKey": "your_api_key", "organization": "your_org" }
    /// </summary>
    public void LoadCredentialsFromFile()
    {
        if (!string.IsNullOrEmpty(apiKey))
            return;

        try
        {
            string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string filePath = Path.Combine(homePath, ".openai", "auth.json");
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var creds = JsonConvert.DeserializeObject<Credentials>(json);
                if (creds != null)
                {
                    apiKey = creds.apiKey;
                    organization = creds.organization;
                }
            }
            else
            {
                Debug.LogWarning($"Credentials file not found at: {filePath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load credentials: {ex.Message}");
        }
    }


    public string GetApiKey() 
    { 
        return apiKey;
    }

    public string GetOrganization()
    {
        return organization;
    }
}

[Serializable]
public class Credentials
{
    public string apiKey;
    public string organization;
}
