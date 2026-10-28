using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Builds the greybox arena, bakes the NavMesh, runs the waves and draws the HUD, menus and subtitles.
// Press Play in any scene: the game boots itself, so there is no scene wiring to break.
public class Game : MonoBehaviour
{
    enum Phase { Menu, Playing, Paused, Won, Lost }

    [Tooltip("Enemies per wave. Tuned for a 5-8 minute run (criterion 7); change after the stopwatch playtests.")]
    public int[] waves = { 3, 4, 6, 8 };
    public float waveBreak = 6f;

    static Game instance;
    public static bool Playing => instance != null && instance.phase == Phase.Playing;
    public static PlayerController Player => instance != null ? instance.player : null;

    const float VirtualHeight = 900f; // HUD is laid out at 900 px tall and scaled to the real screen
    static readonly Vector3 PlayerStart = new Vector3(0f, 0.05f, -25f);

    Phase phase;
    PlayerController player;
    int wave = -1, alive;
    float runTime, nextWaveAt = -1f;
    readonly List<Vector3> spawnPoints = new List<Vector3>(), patrolPoints = new List<Vector3>();
    readonly string[] captions = new string[2];
    readonly float[] captionUntil = new float[2];
    Vector3 damageFrom;
    float damageUntil;
    NavMeshDataInstance navMesh;
    GUIStyle panel, captionStyle, titleStyle, centered, small;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded += (scene, mode) => Ensure();
        Ensure();
    }

    static void Ensure()
    {
        if (FindAnyObjectByType<Game>() == null) new GameObject("Game").AddComponent<Game>();
    }

    void Awake()
    {
        instance = this;
        foreach (var cam in Camera.allCameras) cam.gameObject.SetActive(false); // the scene's own camera, if it has one
        gameObject.AddComponent<Feel>();
        BuildArena();
        BakeNavMesh();
        SpawnPlayer();
        SetPhase(Phase.Menu);
    }

    void OnDestroy()
    {
        navMesh.Remove(); // runtime NavMesh data outlives scenes unless removed
        if (instance == this) instance = null;
    }

    void OnApplicationQuit() => GameSettings.Save();

    // ---------- Arena ----------

    static readonly Color FloorColor = new Color(0.42f, 0.44f, 0.46f);
    static readonly Color WallColor = new Color(0.62f, 0.62f, 0.64f);
    static readonly Color CoverColor = new Color(0.74f, 0.71f, 0.64f);
    static readonly Color PlatformColor = new Color(0.5f, 0.55f, 0.6f);

    // A fixed layout (not random) so every playtest is the same arena.
    void BuildArena()
    {
        Block(new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), FloorColor);
        Block(new Vector3(0f, 2.5f, 30.5f), new Vector3(62f, 5f, 1f), WallColor);
        Block(new Vector3(0f, 2.5f, -30.5f), new Vector3(62f, 5f, 1f), WallColor);
        Block(new Vector3(30.5f, 2.5f, 0f), new Vector3(1f, 5f, 62f), WallColor);
        Block(new Vector3(-30.5f, 2.5f, 0f), new Vector3(1f, 5f, 62f), WallColor);

        // Central platform (2 m) with ramps north and south: a downhill slide speeds up (g sin theta).
        Block(new Vector3(0f, 1f, 0f), new Vector3(8f, 2f, 8f), PlatformColor);
        float rampAngle = Mathf.Atan2(2f, 6f) * Mathf.Rad2Deg; // rises 2 m over 6 m, about 18 degrees
        float rampLength = Mathf.Sqrt(2f * 2f + 6f * 6f);
        Block(new Vector3(0f, 0.95f, -7f), new Vector3(3f, 0.3f, rampLength), PlatformColor, Quaternion.Euler(-rampAngle, 0f, 0f));
        Block(new Vector3(0f, 0.95f, 7f), new Vector3(3f, 0.3f, rampLength), PlatformColor, Quaternion.Euler(rampAngle, 0f, 0f));

        // Low cover (1.1 m): hides a crouching player only.
        float[,] low = { { -10, -15, 4, 1 }, { 10, -15, 4, 1 }, { 0, -19, 6, 1 }, { -18, -5, 1, 4 }, { 18, -5, 1, 4 }, { -8, 13, 4, 1 }, { 8, 13, 4, 1 } };
        for (int i = 0; i < low.GetLength(0); i++)
            Block(new Vector3(low[i, 0], 0.55f, low[i, 1]), new Vector3(low[i, 2], 1.1f, low[i, 3]), CoverColor);

        // Full cover (3 m).
        float[,] tall = { { -14, 7, 2, 2 }, { 14, 7, 2, 2 }, { -22, 18, 6, 1 }, { 22, 18, 6, 1 }, { -22, -18, 1, 6 }, { 22, -18, 1, 6 }, { 0, 22, 8, 1 }, { -8, -6, 2, 2 }, { 8, -6, 2, 2 } };
        for (int i = 0; i < tall.GetLength(0); i++)
            Block(new Vector3(tall[i, 0], 1.5f, tall[i, 1]), new Vector3(tall[i, 2], 3f, tall[i, 3]), CoverColor);

        // Overhangs with a 1.2 m gap underneath: only passable crouched or sliding.
        Block(new Vector3(-25f, 1.7f, 4f), new Vector3(6f, 1f, 3f), WallColor);
        Block(new Vector3(25f, 1.7f, 4f), new Vector3(6f, 1f, 3f), WallColor);

        spawnPoints.AddRange(new[] { new Vector3(-25f, 0f, 25f), new Vector3(25f, 0f, 25f), new Vector3(0f, 0f, 26f), new Vector3(-26f, 0f, -2f), new Vector3(26f, 0f, -2f), new Vector3(-12f, 0f, 20f), new Vector3(12f, 0f, 20f) });

        if (FindAnyObjectByType<Light>() == null)
        {
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    static void Block(Vector3 position, Vector3 size, Color color, Quaternion? rotation = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Greybox";
        go.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().material.color = color;
    }

    void BakeNavMesh()
    {
        Physics.SyncTransforms();
        var bounds = new Bounds(Vector3.zero, new Vector3(70f, 20f, 70f));
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
        navMesh = NavMesh.AddNavMeshData(data);

        for (float x = -24f; x <= 24f; x += 8f)
            for (float z = -16f; z <= 24f; z += 8f)
                if (NavMesh.SamplePosition(new Vector3(x, 0f, z), out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    patrolPoints.Add(hit.position);
    }

    public static Vector3 RandomPatrolPoint()
    {
        var points = instance.patrolPoints;
        return points.Count == 0 ? Vector3.zero : points[Random.Range(0, points.Count)];
    }

    // ---------- Run flow ----------

    void SpawnPlayer()
    {
        if (player != null) Destroy(player.gameObject);
        var go = new GameObject("Player");
        go.transform.SetPositionAndRotation(PlayerStart, Quaternion.identity); // before the CharacterController exists
        player = go.AddComponent<PlayerController>();
    }

    void StartRun()
    {
        foreach (var e in EnemyAI.All.ToArray()) Destroy(e.gameObject);
        wave = -1;
        alive = 0;
        runTime = 0f;
        nextWaveAt = -1f;
        GameSettings.Save();
        SetPhase(Phase.Playing);
    }

    void StartWave()
    {
        wave++;
        int count = waves[wave];
        Caption($"Wave {wave + 1} of {waves.Length}: {count} enemies");
        Vector3 p = player.transform.position;
        spawnPoints.Sort((a, b) => (b - p).sqrMagnitude.CompareTo((a - p).sqrMagnitude)); // farthest first
        for (int i = 0; i < count; i++)
        {
            Vector3 spot = spawnPoints[i % spawnPoints.Count] + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
            if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                EnemyAI.Spawn(hit.position);
                alive++;
            }
        }
    }

    public static void EnemyKilled()
    {
        if (instance != null) instance.alive--;
    }

    public static void Lose()
    {
        if (Playing) instance.SetPhase(Phase.Lost);
    }

    void Win()
    {
        var s = GameSettings.Current;
        if (s.bestTime <= 0f || runTime < s.bestTime) s.bestTime = runTime;
        GameSettings.Save();
        SetPhase(Phase.Won);
    }

    void SetPhase(Phase next)
    {
        phase = next;
        bool playing = next == Phase.Playing;
        Time.timeScale = playing ? 1f : 0f;
        Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !playing;
        if (!playing) GameSettings.Save();
    }

    void Update()
    {
        var s = GameSettings.Current;
        if (Controls.ToggleOverlay) s.debugOverlay = !s.debugOverlay;
        if (Controls.ToggleFeel)
        {
            s.feel = !s.feel;
            Caption(s.feel ? "Feel layer ON" : "Feel layer OFF");
        }
        if (Controls.TogglePooling)
        {
            ProjectileWeapon.UsePooling = !ProjectileWeapon.UsePooling;
            Caption(ProjectileWeapon.UsePooling ? "Object pooling ON" : "Object pooling OFF (profiling)");
        }

        switch (phase)
        {
            case Phase.Menu:
                if (Controls.Confirm) StartRun();
                break;
            case Phase.Paused:
                if (Controls.Pause) SetPhase(Phase.Playing);
                break;
            case Phase.Won:
            case Phase.Lost:
                if (Controls.Confirm) Restart();
                break;
            case Phase.Playing:
                if (Controls.Pause) { SetPhase(Phase.Paused); break; }
                runTime += Time.unscaledDeltaTime; // hit-stop doesn't slow the clock
                if (alive <= 0 && nextWaveAt < 0f)
                {
                    if (wave >= waves.Length - 1) { Win(); break; }
                    nextWaveAt = runTime + (wave < 0 ? 3f : waveBreak);
                    if (wave >= 0) Caption($"Wave cleared. Next wave in {waveBreak:0} seconds");
                }
                if (nextWaveAt >= 0f && runTime >= nextWaveAt)
                {
                    nextWaveAt = -1f;
                    StartWave();
                }
                break;
        }
    }

    void Restart()
    {
        SpawnPlayer();
        StartRun();
    }

    // ---------- Subtitles and damage direction ----------

    // Subtitles say where a sound came from, so a muted player gets the same information.
    public static void Caption(string text, Vector3? source = null)
    {
        if (instance == null) return;
        if (source.HasValue && instance.player != null) text += " (" + instance.Direction(source.Value) + ")";
        float until = Time.unscaledTime + 2.5f;
        var c = instance.captions;
        if (text == c[1]) { instance.captionUntil[1] = until; return; }
        c[0] = c[1];
        instance.captionUntil[0] = instance.captionUntil[1];
        c[1] = text;
        instance.captionUntil[1] = until;
    }

    string Direction(Vector3 from)
    {
        Vector3 local = player.transform.InverseTransformPoint(from);
        if (Mathf.Abs(local.x) > Mathf.Abs(local.z)) return local.x > 0f ? "right" : "left";
        return local.z > 0f ? "ahead" : "behind";
    }

    public static void ShowDamageFrom(Vector3 from)
    {
        if (instance == null) return;
        instance.damageFrom = from;
        instance.damageUntil = Time.unscaledTime + 1f;
    }

    // ---------- HUD and menus (IMGUI) ----------
    // ponytail: IMGUI allocates a little garbage every frame; move the HUD to UI Toolkit if the Profiler shows it in the GC column.

    void OnGUI()
    {
        Styles();
        float scale = Screen.height / VirtualHeight;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float w = Screen.width / scale, h = VirtualHeight;
        var s = GameSettings.Current;

        switch (phase)
        {
            case Phase.Menu:
                GUI.Label(new Rect(0f, h * 0.12f, w, 60f), "ARENA SLICE", titleStyle);
                GUI.Label(new Rect(0f, h * 0.12f + 60f, w, 30f),
                    $"Survive {waves.Length} waves." + (s.bestTime > 0f ? $"  Best time {Clock(s.bestTime)}" : ""), centered);
                GUI.Label(new Rect(w / 2f - 420f, h * 0.28f, 840f, 110f),
                    "WASD move   Mouse look   Shift sprint   Ctrl / C crouch (while sprinting: slide)   Space jump\n" +
                    "Left mouse fire   Right mouse aim   R reload   1 / 2 or wheel switch weapon   Esc pause\n" +
                    "F1 AI state overlay   F2 feel layer on/off   F3 object pooling on/off", centered);
                if (GUI.Button(new Rect(w / 2f - 110f, h * 0.42f, 220f, 44f), "Start (Enter)")) StartRun();
                DrawSettings(new Rect(w / 2f - 210f, h * 0.42f + 60f, 420f, 300f), s, false);
                break;
            case Phase.Playing:
                DrawHud(w, h, s, scale);
                break;
            case Phase.Paused:
                DrawHud(w, h, s, scale);
                DrawSettings(new Rect(w / 2f - 210f, h / 2f - 190f, 420f, 380f), s, true);
                break;
            case Phase.Won:
            case Phase.Lost:
                GUI.Label(new Rect(0f, h * 0.3f, w, 60f), phase == Phase.Won ? "ARENA CLEARED" : "YOU DIED", titleStyle);
                GUI.Label(new Rect(0f, h * 0.3f + 70f, w, 60f),
                    $"Time {Clock(runTime)}   Wave {Mathf.Max(wave + 1, 1)} of {waves.Length}" +
                    (s.bestTime > 0f ? $"\nBest time {Clock(s.bestTime)}" : ""), centered);
                if (GUI.Button(new Rect(w / 2f - 110f, h * 0.3f + 150f, 220f, 44f), "Play again (Enter)")) Restart();
                break;
        }

        // Subtitles: white text on an opaque black box, bottom centre.
        if (s.subtitles)
        {
            float y = h - 150f;
            for (int i = 1; i >= 0; i--)
            {
                if (captions[i] == null || Time.unscaledTime > captionUntil[i]) continue;
                Vector2 size = captionStyle.CalcSize(new GUIContent(captions[i]));
                GUI.Label(new Rect((w - size.x) / 2f, y, size.x, size.y), captions[i], captionStyle);
                y -= size.y + 4f;
            }
        }
    }

    void DrawHud(float w, float h, GameSettings s, float scale)
    {
        if (player == null) return;
        Weapon gun = player.CurrentWeapon;

        // Crosshair gap shows the real current spread: tan(spread) / tan(vertical FOV / 2) of half the screen height.
        float gap = 4f + Mathf.Tan(gun.CurrentSpread * Mathf.Deg2Rad) / Mathf.Tan(player.Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * (h / 2f);
        Vector2 c = new Vector2(w / 2f, h / 2f);
        Bar(new Rect(c.x - 1f, c.y - gap - 8f, 2f, 8f));
        Bar(new Rect(c.x - 1f, c.y + gap, 2f, 8f));
        Bar(new Rect(c.x - gap - 8f, c.y - 1f, 8f, 2f));
        Bar(new Rect(c.x + gap, c.y - 1f, 8f, 2f));

        // Health: number and bar length, not colour.
        Health hp = player.Health;
        GUI.Box(new Rect(20f, h - 70f, 240f, 50f), GUIContent.none, panel);
        GUI.Label(new Rect(30f, h - 68f, 220f, 24f), $"HEALTH {Mathf.CeilToInt(hp.Current)}", small);
        GUI.DrawTexture(new Rect(30f, h - 42f, 220f * hp.Current / hp.max, 10f), Texture2D.whiteTexture);

        string ammo = gun.Reloading ? "RELOADING" : $"{gun.Ammo} / {gun.magazine}" + (gun.Ammo <= gun.magazine / 4 ? "  LOW" : "");
        GUI.Box(new Rect(w - 260f, h - 70f, 240f, 50f), GUIContent.none, panel);
        GUI.Label(new Rect(w - 250f, h - 68f, 220f, 46f), $"{gun.displayName}\n{ammo}", small);

        string status = wave < 0 ? "Get ready" : $"Wave {wave + 1} / {waves.Length}   Enemies {alive}";
        GUI.Label(new Rect(0f, 14f, w, 26f), $"{status}   {Clock(runTime)}", centered);

        if (Time.unscaledTime < damageUntil)
        {
            string dir = Direction(damageFrom);
            string arrow = dir == "left" ? "<<  HIT" : dir == "right" ? "HIT  >>" : dir == "behind" ? "HIT (behind)" : "HIT (ahead)";
            GUI.Label(new Rect(c.x - 150f, c.y + 40f, 300f, 26f), arrow, centered);
        }

        // Every enemy shows "?" (searching) or "!" (knows where you are) for the player to read. F1 adds the exact state for the criterion 3 test.
        foreach (var e in EnemyAI.All)
        {
            Vector3 p = player.Cam.WorldToScreenPoint(e.transform.position + Vector3.up * 2.4f);
            if (p.z < 0f) continue;
            string mark = e.Current >= EnemyAI.State.Chase ? "!" : e.Current >= EnemyAI.State.Investigate ? "?" : "";
            string label = s.debugOverlay ? $"{mark} {e.Current}".Trim() : mark;
            if (label.Length > 0) GUI.Label(new Rect(p.x / scale - 60f, (Screen.height - p.y) / scale - 12f, 120f, 24f), label, centered);
        }

        if (s.debugOverlay)
            GUI.Label(new Rect(20f, 50f, 400f, 60f),
                $"Player: {player.Current}  {new Vector2(player.Velocity.x, player.Velocity.z).magnitude:0.0} m/s\n" +
                $"Pooling {(ProjectileWeapon.UsePooling ? "ON" : "OFF")}   Feel {(s.feel ? "ON" : "OFF")}", small);
    }

    void DrawSettings(Rect area, GameSettings s, bool paused)
    {
        GUILayout.BeginArea(area, panel);
        GUILayout.Label(paused ? "PAUSED" : "Settings", small);
        GUILayout.Label($"Field of view (horizontal): {s.fov:0}°");
        s.fov = GUILayout.HorizontalSlider(s.fov, 60f, 120f);
        GUILayout.Label($"Mouse sensitivity: {s.sensitivity:0.0}");
        s.sensitivity = GUILayout.HorizontalSlider(s.sensitivity, 0.2f, 6f);
        GUILayout.Label($"Aim (ADS) sensitivity: {s.adsSensitivity:0.0}");
        s.adsSensitivity = GUILayout.HorizontalSlider(s.adsSensitivity, 0.2f, 6f);
        GUILayout.Space(6f);
        s.subtitles = GUILayout.Toggle(s.subtitles, " Subtitles");
        s.feel = GUILayout.Toggle(s.feel, " Feel layer (hit-stop, shake, FOV kick, markers, numbers)");
        s.debugOverlay = GUILayout.Toggle(s.debugOverlay, " AI state overlay");
        if (paused)
        {
            GUILayout.Space(10f);
            if (GUILayout.Button("Resume (Esc)")) SetPhase(Phase.Playing);
            if (GUILayout.Button("Restart")) Restart();
        }
        GUILayout.EndArea();
    }

    static void Bar(Rect r) => GUI.DrawTexture(r, Texture2D.whiteTexture);

    static string Clock(float t) => $"{(int)(t / 60f):00}:{(int)(t % 60f):00}";

    void Styles()
    {
        if (panel != null) return;
        var black = new Texture2D(1, 1);
        black.SetPixel(0, 0, Color.black);
        black.Apply();
        var dim = new Texture2D(1, 1);
        dim.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.7f));
        dim.Apply();

        panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10) };
        panel.normal.background = dim;
        captionStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, padding = new RectOffset(12, 12, 6, 6), wordWrap = false };
        captionStyle.normal.background = black;
        captionStyle.normal.textColor = Color.white;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        centered = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        small = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
    }
}
