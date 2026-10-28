using UnityEngine;

// Everything a gun shares: fire rate, magazine, reload, spread, recoil, falloff, headshots and noise.
// A subclass only decides what one shot physically is (FireShot). Criterion 2 test: a third weapon should need no edits here.
public abstract class Weapon : MonoBehaviour
{
    [Header("Handling")]
    public string displayName = "Weapon";
    public bool automatic = true;
    public float roundsPerMinute = 600f;
    public int magazine = 30;
    public float reloadTime = 1.8f;
    public float adsZoom = 1.3f;

    [Header("Damage")]
    public float damage = 25f;
    public float headMultiplier = 2f;
    public float falloffStart = 20f, falloffEnd = 50f;
    [Range(0f, 1f)] public float falloffMinFraction = 0.5f;

    [Header("Accuracy (degrees)")]
    public float hipSpread = 1.5f, adsSpread = 0.3f, movementSpread = 2f;

    [Header("Recoil (degrees per shot)")]
    public float recoilPitch = 1f, recoilYaw = 0.3f;

    [Header("Noise radius the AI can hear (m)")]
    public float noiseRadius = 40f;

    public int Ammo { get; private set; }
    public bool Reloading => reloadEnd > 0f;
    public float CurrentSpread { get; private set; }
    protected PlayerController Owner { get; private set; }
    protected Transform Muzzle { get; private set; }
    float nextShot, reloadEnd;

    // Called after the fields are configured, so Ammo starts at the real magazine size.
    public void Init(PlayerController owner, Transform muzzle)
    {
        Owner = owner;
        Muzzle = muzzle;
        Ammo = magazine;
    }

    protected abstract void FireShot(Vector3 origin, Vector3 direction);
    protected abstract AudioClip ShotSound { get; }

    void OnDisable() => reloadEnd = 0f; // switching weapons cancels a reload

    public void StartReload()
    {
        if (!Reloading && Ammo < magazine) reloadEnd = Time.time + reloadTime;
    }

    public void Tick(bool trigger, bool triggerDown, bool aiming)
    {
        if (Reloading && Time.time >= reloadEnd) { reloadEnd = 0f; Ammo = magazine; }

        CurrentSpread = (aiming ? adsSpread : hipSpread) + movementSpread * Owner.SpreadFactor;

        if (!(automatic ? trigger : triggerDown) || Reloading || Time.time < nextShot) return;
        if (Ammo <= 0)
        {
            Owner.Audio.PlayOneShot(Sfx.Empty);
            StartReload();
            return;
        }

        nextShot = Time.time + 60f / roundsPerMinute;
        Ammo--;
        Transform cam = Owner.Cam.transform;
        FireShot(cam.position, SpreadDirection(cam, CurrentSpread));
        Owner.AddRecoil(recoilPitch * (aiming ? 0.6f : 1f), Random.Range(-recoilYaw, recoilYaw));
        Owner.Audio.PlayOneShot(ShotSound);
        EnemyAI.Hear(Owner.transform.position, noiseRadius);
    }

    // Uniform random point in a cone: offset the forward vector by a point in a disc of radius tan(spread).
    static Vector3 SpreadDirection(Transform cam, float spreadDegrees)
    {
        Vector2 r = Random.insideUnitCircle * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
        return (cam.forward + cam.right * r.x + cam.up * r.y).normalized;
    }

    // Linear falloff from full damage at falloffStart to falloffMinFraction at falloffEnd.
    public float DamageAt(float distance, bool head)
    {
        float falloff = Mathf.Lerp(1f, falloffMinFraction, Mathf.InverseLerp(falloffStart, falloffEnd, distance));
        return damage * falloff * (head ? headMultiplier : 1f);
    }

    public void ApplyHit(Collider c, Vector3 point, float distance)
    {
        var target = c.GetComponentInParent<Health>();
        if (target == null || target == Owner.Health || target.Dead) return;
        bool head = Health.IsHead(c);
        float amount = DamageAt(distance, head);
        target.Damage(amount, Owner.transform.position);
        Owner.Audio.PlayOneShot(head ? Sfx.Headshot : Sfx.Hit);
        Feel.Hit(point, amount, head, target.Dead);
    }
}
