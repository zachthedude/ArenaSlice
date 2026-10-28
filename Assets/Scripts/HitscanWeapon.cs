using UnityEngine;

// The bullet arrives instantly: a raycast from the camera along the spread direction.
public class HitscanWeapon : Weapon
{
    public float range = 200f;
    Tracer tracer;

    protected override AudioClip ShotSound => Sfx.RifleShot;

    void Start() => tracer = Tracer.Create(new Color(1f, 0.9f, 0.5f));
    void OnDestroy() { if (tracer) Destroy(tracer.gameObject); }

    protected override void FireShot(Vector3 origin, Vector3 direction)
    {
        Vector3 end = origin + direction * range;
        // DefaultRaycastLayers skips the player's own Ignore Raycast layer.
        if (Physics.Raycast(origin, direction, out RaycastHit hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            ApplyHit(hit.collider, hit.point, hit.distance);
        }
        tracer.Show(Muzzle.position, end);
    }
}
