using UnityEngine;

// The "juice" layer: hit-stop, screen shake, FOV kick, hit markers and damage numbers, scaled by body/head/kill.
// GameSettings.feel (F2, or the pause menu) removes the whole layer: criterion 4 says the game must still be playable without it.
public class Feel : MonoBehaviour
{
    static Feel instance;

    [Header("Individual effects")]
    public bool hitStop = true, shake = true, fovKick = true, hitMarkers = true, damageNumbers = true;

    struct Number { public Vector3 position; public float value, born; public bool head; }
    readonly Number[] numbers = new Number[24]; // fixed ring buffer: no allocation per hit
    int nextNumber;
    float shakeDegrees, kick, stopUntil, markerUntil;
    bool markerHead, markerKill;
    GUIStyle numberStyle, markerStyle;

    static bool On => instance != null && GameSettings.Current.feel;
    public static Quaternion ShakeRotation => On && instance.shake && instance.shakeDegrees > 0.001f
        ? Quaternion.Euler(Random.insideUnitSphere * instance.shakeDegrees) : Quaternion.identity;
    public static float FovKick => On && instance.fovKick ? instance.kick : 0f;

    void Awake() => instance = this;

    public static void Hit(Vector3 point, float damage, bool head, bool kill)
    {
        if (!On) return;
        var f = instance;
        float scale = (head ? 2f : 1f) * (kill ? 1.5f : 1f);
        if (f.hitStop) f.stopUntil = Time.unscaledTime + 0.025f * scale;
        f.shakeDegrees = Mathf.Max(f.shakeDegrees, 0.2f * scale);
        f.kick = Mathf.Max(f.kick, 1.5f * scale);
        f.markerUntil = Time.unscaledTime + 0.15f + 0.1f * scale;
        f.markerHead = head;
        f.markerKill = kill;
        f.numbers[f.nextNumber] = new Number { position = point, value = damage, born = Time.unscaledTime, head = head };
        f.nextNumber = (f.nextNumber + 1) % f.numbers.Length;
    }

    public static void PlayerHit(float damage)
    {
        if (On) instance.shakeDegrees = Mathf.Max(instance.shakeDegrees, 0.05f * damage);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (Game.Playing) Time.timeScale = On && Time.unscaledTime < stopUntil ? 0.05f : 1f;
        shakeDegrees = Mathf.MoveTowards(shakeDegrees, 0f, 4f * dt);
        kick = Mathf.MoveTowards(kick, 0f, 20f * dt);
    }

    void OnGUI()
    {
        if (!On || !Game.Playing || Game.Player == null) return;
        numberStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        markerStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 14 };
        float now = Time.unscaledTime;
        Vector2 c = new Vector2(Screen.width / 2f, Screen.height / 2f);

        // Hit marker: shape and words carry the meaning (bigger X + "HEADSHOT"/"KILL"), never colour alone.
        if (hitMarkers && now < markerUntil)
        {
            float size = markerHead ? 14f : 9f;
            DrawX(c, size, markerKill ? 3f : 2f);
            if (markerHead || markerKill) GUI.Label(new Rect(c.x - 60f, c.y + 18f, 120f, 20f), markerKill ? "KILL" : "HEADSHOT", markerStyle);
        }

        if (!damageNumbers) return;
        Camera cam = Game.Player.Cam;
        for (int i = 0; i < numbers.Length; i++)
        {
            float age = now - numbers[i].born;
            if (numbers[i].value <= 0f || age > 0.8f) continue;
            Vector3 s = cam.WorldToScreenPoint(numbers[i].position + Vector3.up * age);
            if (s.z < 0f) continue;
            numberStyle.fontSize = numbers[i].head ? 22 : 15;
            string text = numbers[i].head ? Mathf.RoundToInt(numbers[i].value) + "!" : Mathf.RoundToInt(numbers[i].value).ToString();
            GUI.Label(new Rect(s.x - 40f, Screen.height - s.y - 12f, 80f, 24f), text, numberStyle);
        }
    }

    static readonly float[] XAngles = { 45f, -45f };

    static void DrawX(Vector2 c, float size, float thickness)
    {
        Matrix4x4 m = GUI.matrix;
        foreach (float angle in XAngles)
        {
            GUIUtility.RotateAroundPivot(angle, c);
            GUI.DrawTexture(new Rect(c.x - size, c.y - thickness / 2f, size * 2f, thickness), Texture2D.whiteTexture);
            GUI.matrix = m;
        }
    }
}
