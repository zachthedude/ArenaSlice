using UnityEngine;

public class Projectile : MonoBehaviour
{
    public const float MaxLifetime = 5f;
    internal bool pooled;
    ProjectileWeapon weapon;
    Vector3 velocity;
    float gravity, travelled, age;

    public void Launch(ProjectileWeapon from, Vector3 origin, Vector3 initialVelocity, float g)
    {
        weapon = from;
        transform.position = origin;
        velocity = initialVelocity;
        gravity = g;
        travelled = 0f;
        age = 0f;
    }

    void FixedUpdate()
    {
        if (weapon == null) { Destroy(gameObject); return; } // owner was destroyed by a restart
        float dt = Time.fixedDeltaTime;
        // Semi-implicit Euler: update velocity, then move. The step is swept with a raycast
        // so a fast shot can't tunnel through a thin wall between two physics ticks.
        velocity += Vector3.down * gravity * dt;
        Vector3 step = velocity * dt;
        float length = step.magnitude;
        Vector3 pos = transform.position;

        if (Physics.Raycast(pos, step, out RaycastHit hit, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            weapon.ApplyHit(hit.collider, hit.point, travelled + hit.distance);
            weapon.Return(this);
            return;
        }

        transform.position = pos + step;
        travelled += length;
        age += dt;
        if (age > MaxLifetime) weapon.Return(this);
    }
}
