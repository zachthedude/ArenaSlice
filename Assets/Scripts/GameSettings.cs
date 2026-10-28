using System.IO;
using UnityEngine;

// Saved as JSON in Application.persistentDataPath/settings.json.
[System.Serializable]
public class GameSettings
{
    public float fov = 90f;
    public float sensitivity = 2f;
    public float adsSensitivity = 1f;
    public bool subtitles = true;
    public bool feel = true;
    public bool debugOverlay = false;
    public float bestTime = 0f;

    static GameSettings current;
    public static GameSettings Current => current ??= Load();
    static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

    static GameSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonUtility.FromJson<GameSettings>(File.ReadAllText(FilePath)) ?? new GameSettings();
        }
        catch (System.Exception e) { Debug.LogWarning($"Settings unreadable, using defaults: {e.Message}"); }
        return new GameSettings();
    }

    public static void Save()
    {
        // Write to a temp file first so a crash mid-write can't leave a half-written settings file.
        string tmp = FilePath + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonUtility.ToJson(Current, true));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }
        catch (System.Exception e) { Debug.LogWarning($"Could not save settings: {e.Message}"); }
    }
}
