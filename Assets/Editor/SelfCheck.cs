using UnityEditor;
using UnityEngine;

// Menu: ArenaSlice > Run Self-Check. Batch: Unity -batchmode -projectPath . -executeMethod SelfCheck.Run -quit
public static class SelfCheck
{
    [MenuItem("ArenaSlice/Run Self-Check")]
    public static void Run()
    {
        // Criterion B, Fig 5: 1.2 m jump at g = 20 -> 6.93 m/s take-off, 0.69 s in the air; real g -> 0.99 s.
        Near(Kinematics.TakeoffSpeed(20f, 1.2f), 6.928f, "take-off speed");
        Near(Kinematics.AirTime(20f, 1.2f), 0.693f, "air time at g=20");
        Near(Kinematics.AirTime(9.81f, 1.2f), 0.989f, "air time at g=9.81");
        // Sight cone: cos 55 degrees.
        Near(Kinematics.ConeThreshold(55f), 0.5736f, "sight cone threshold");

        var go = new GameObject("check");
        try
        {
            var w = go.AddComponent<HitscanWeapon>();
            w.damage = 20f; w.headMultiplier = 2f; w.falloffStart = 10f; w.falloffEnd = 30f; w.falloffMinFraction = 0.5f;
            Near(w.DamageAt(5f, false), 20f, "full damage inside falloff start");
            Near(w.DamageAt(20f, false), 15f, "half-way through falloff");
            Near(w.DamageAt(100f, true), 20f, "headshot at max falloff");
        }
        finally { Object.DestroyImmediate(go); }

        Debug.Log("ArenaSlice self-check passed");
    }

    static void Near(float actual, float expected, string what)
    {
        if (Mathf.Abs(actual - expected) > 0.001f)
            throw new System.Exception($"Self-check failed: {what} = {actual}, expected {expected}");
    }
}
