using UnityEngine;

// Placeholder sounds generated in code (decaying noise and tones), so the slice has audio for the muted/subtitle test without an asset pack.
public static class Sfx
{
    public static readonly AudioClip RifleShot = Make("rifle", 0.22f, 1f, 28f);
    public static readonly AudioClip LauncherShot = Make("launcher", 0.45f, 0.6f, 9f, 85f);
    public static readonly AudioClip EnemyShot = Make("enemy", 0.2f, 0.8f, 32f, 0f);
    public static readonly AudioClip Hit = Make("hit", 0.05f, 0f, 60f, 1800f);
    public static readonly AudioClip Headshot = Make("head", 0.09f, 0f, 35f, 2600f);
    public static readonly AudioClip Alert = Make("alert", 0.18f, 0f, 12f, 700f);
    public static readonly AudioClip Step = Make("step", 0.08f, 0.5f, 70f);
    public static readonly AudioClip Empty = Make("empty", 0.04f, 0.2f, 90f, 400f);

    static AudioClip Make(string name, float seconds, float noise, float decay, float toneHz = 0f)
    {
        const int rate = 44100;
        int n = (int)(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(name.Length * 7919);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float tone = toneHz > 0f ? Mathf.Sin(2f * Mathf.PI * toneHz * t) : 0f;
            data[i] = 0.5f * Mathf.Exp(-decay * t) * (noise * ((float)rng.NextDouble() * 2f - 1f) + tone);
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
