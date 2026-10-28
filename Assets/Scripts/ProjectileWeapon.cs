using UnityEngine;
using UnityEngine.Pool;

// Fires a real ballistic projectile with muzzle velocity and gravity drop.
public class ProjectileWeapon : Weapon
{
    public float muzzleVelocity = 60f;
    public float projectileGravity = 9.81f; // real g: at 60 m/s a 40 m shot drops about 2.2 m

    // F3 toggles this during play so the Profiler can capture before/after pooling (criterion 5).
    public static bool UsePooling = true;
    ObjectPool<Projectile> pool;

    protected override AudioClip ShotSound => Sfx.LauncherShot;

    void Awake()
    {
        pool = new ObjectPool<Projectile>(Create,
            p => p.gameObject.SetActive(true),
            p => p.gameObject.SetActive(false),
            p => Destroy(p.gameObject),
            true, 16, 64);
        // Warm the pool up front so the first shots don't allocate mid-fight.
        var warm = new Projectile[16];
        for (int i = 0; i < warm.Length; i++) warm[i] = pool.Get();
        foreach (var p in warm) pool.Release(p);
    }

    void OnDestroy() => pool.Clear(); // destroys the idle projectiles too

    Projectile Create()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Projectile";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.15f;
        go.GetComponent<Renderer>().material.color = new Color(1f, 0.5f, 0.1f);
        return go.AddComponent<Projectile>();
    }

    protected override void FireShot(Vector3 origin, Vector3 direction)
    {
        Projectile p = UsePooling ? pool.Get() : Create();
        p.pooled = UsePooling;
        // Galilean addition: the shot also carries the shooter's own velocity.
        p.Launch(this, origin, direction * muzzleVelocity + Owner.Velocity, projectileGravity);
    }

    public void Return(Projectile p)
    {
        if (p.pooled) pool.Release(p);
        else Destroy(p.gameObject);
    }
}
