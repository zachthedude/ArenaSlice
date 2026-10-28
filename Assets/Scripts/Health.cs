using System;
using UnityEngine;

// Shared by the player and enemies. Colliders named "Head" on the same object count as headshots.
public class Health : MonoBehaviour
{
    public float max = 100f;
    public float regenDelay = 0f, regenPerSecond = 0f;

    public float Current { get; private set; }
    public bool Dead => Current <= 0f;
    public event Action<float, Vector3> Damaged; // amount, attacker position
    public event Action Died;
    float lastHit;

    public static bool IsHead(Collider c) => c.name == "Head";

    void Awake() => Current = max;

    void Update()
    {
        if (regenPerSecond > 0f && !Dead && Time.time - lastHit > regenDelay)
            Current = Mathf.Min(max, Current + regenPerSecond * Time.deltaTime);
    }

    public void Damage(float amount, Vector3 from)
    {
        if (Dead) return;
        Current = Mathf.Max(0f, Current - amount);
        lastHit = Time.time;
        Damaged?.Invoke(amount, from);
        if (Dead) Died?.Invoke();
    }
}
