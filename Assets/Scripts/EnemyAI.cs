using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Six-state enemy. Ordered so that "below Chase" means the enemy does not know where the player is.
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    public enum State { Idle, Patrol, Investigate, Search, Chase, Attack }
    public static readonly List<EnemyAI> All = new List<EnemyAI>();

    [Header("Senses")]
    public float sightRange = 30f;
    public float sightHalfAngle = 55f;
    public float reactionTime = 0.4f; // how long the player must be seen before an unaware enemy reacts

    [Header("Combat")]
    public float attackRange = 18f;
    public float fireInterval = 0.9f;
    public float damage = 8f;
    public float aimError = 2.5f; // degrees

    [Header("Movement (m/s)")]
    public float patrolSpeed = 2.5f, chaseSpeed = 5f;

    public State Current { get; private set; }
    public Vector3 EyePosition => transform.position + Vector3.up * 1.8f;

    NavMeshAgent agent;
    Health health;
    AudioSource audioSource;
    Tracer tracer;
    Vector3 lastKnown;
    float coneDot, seenFor, lookTimer, nextShot;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
        audioSource = GetComponent<AudioSource>();
        coneDot = Kinematics.ConeThreshold(sightHalfAngle); // cos 55° ≈ 0.57
        tracer = Tracer.Create(new Color(1f, 0.3f, 0.2f));
        health.Damaged += (amount, from) =>
        {
            lastKnown = from; // a shot gives away where it came from
            if (Current < State.Chase) Enter(State.Chase);
        };
        health.Died += Die;
        Enter(State.Idle);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);
    void OnDestroy() { if (tracer) Destroy(tracer.gameObject); }

    // Sound is a sphere: any unaware enemy inside the radius goes to look. ponytail: walls don't muffle it; raycast-occlude if playtests say it feels unfair.
    public static void Hear(Vector3 position, float radius)
    {
        if (radius <= 0f) return;
        for (int i = 0; i < All.Count; i++) All[i].OnSound(position, radius);
    }

    void OnSound(Vector3 position, float radius)
    {
        if (Current >= State.Chase || (position - transform.position).sqrMagnitude > radius * radius) return;
        if (Current != State.Investigate) Game.Caption("Enemy: \"What was that?\"", transform.position);
        lastKnown = position;
        Enter(State.Investigate);
    }

    bool CanSeePlayer(PlayerController player)
    {
        Vector3 eye = EyePosition;
        Vector3 to = player.EyePosition - eye;
        float distance = to.magnitude;
        if (distance > sightRange) return false;
        Vector3 dir = to / distance;
        if (Vector3.Dot(transform.forward, dir) < coneDot) return false; // outside the sight cone
        // Line of sight: the first thing the ray hits must be the player (~0 includes the player's Ignore Raycast layer).
        return Physics.Raycast(eye, dir, out RaycastHit hit, distance + 0.5f, ~0, QueryTriggerInteraction.Ignore)
               && hit.collider.transform == player.transform;
    }

    void Enter(State next)
    {
        if (next == State.Chase && Current < State.Chase)
        {
            Game.Caption("Enemy: \"Contact!\"", transform.position);
            audioSource.PlayOneShot(Sfx.Alert);
        }
        Current = next;
        lookTimer = 0f;
        if (!agent.isOnNavMesh) return;
        agent.isStopped = next == State.Idle || next == State.Attack;
        agent.speed = next >= State.Chase ? chaseSpeed : patrolSpeed;
        if (next == State.Patrol) agent.SetDestination(Game.RandomPatrolPoint());
        if (next == State.Investigate || next == State.Search || next == State.Chase) agent.SetDestination(lastKnown);
    }

    bool Arrived => !agent.pathPending && agent.remainingDistance < 1f;

    void Update()
    {
        var player = Game.Player;
        if (!Game.Playing || player == null || health.Dead) return;
        float dt = Time.deltaTime;

        bool sees = CanSeePlayer(player);
        seenFor = sees ? seenFor + dt : 0f;
        if (sees) lastKnown = player.transform.position;
        float distance = Vector3.Distance(transform.position, player.transform.position);

        switch (Current)
        {
            case State.Idle:
                lookTimer += dt;
                if (lookTimer > 2f) Enter(State.Patrol);
                break;
            case State.Patrol:
                if (Arrived) agent.SetDestination(Game.RandomPatrolPoint());
                break;
            case State.Investigate:
            case State.Search:
                if (!Arrived) break;
                // At the spot: turn on the spot to scan, then give up.
                lookTimer += dt;
                transform.Rotate(0f, 120f * dt, 0f);
                if (lookTimer > (Current == State.Search ? 8f : 5f)) Enter(State.Patrol);
                break;
            case State.Chase:
                agent.SetDestination(lastKnown);
                if (sees && distance < attackRange) Enter(State.Attack);
                else if (!sees && Arrived) Enter(State.Search);
                break;
            case State.Attack:
                if (!sees || distance > attackRange * 1.2f) { Enter(State.Chase); break; }
                Vector3 flat = player.transform.position - transform.position;
                flat.y = 0f;
                if (flat != Vector3.zero)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 360f * dt);
                if (Time.time >= nextShot) Shoot(player);
                break;
        }

        if (Current < State.Chase && seenFor >= reactionTime) Enter(State.Chase);
    }

    void Shoot(PlayerController player)
    {
        nextShot = Time.time + fireInterval * Random.Range(0.8f, 1.2f);
        Vector3 eye = EyePosition;
        Vector3 target = player.transform.position + Vector3.up * 1.1f; // centre of mass
        // Same cone maths as the player's spread; a moving player is harder to hit.
        float error = aimError * (1f + player.Velocity.magnitude / 4f);
        Vector2 r = Random.insideUnitCircle * Mathf.Tan(error * Mathf.Deg2Rad);
        Vector3 dir = (target - eye).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
        dir = (dir + right * r.x + Vector3.Cross(dir, right) * r.y).normalized;

        Vector3 end = eye + dir * 100f;
        if (Physics.Raycast(eye, dir, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            var h = hit.collider.GetComponentInParent<Health>();
            if (h != null && h != health) h.Damage(damage, transform.position);
        }
        tracer.Show(eye + transform.forward * 0.4f, end);
        audioSource.PlayOneShot(Sfx.EnemyShot);
        Game.Caption("[Enemy gunfire]", transform.position);
    }

    void Die()
    {
        All.Remove(this);
        agent.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        transform.Rotate(80f, 0f, 0f); // topple
        Game.EnemyKilled();
        Destroy(gameObject, 3f);
    }

    // Spawns a greybox enemy: pivot at the feet (where the NavMeshAgent stands), body capsule, a separate "Head" collider and a visor showing facing.
    public static EnemyAI Spawn(Vector3 position)
    {
        var root = new GameObject("Enemy");
        root.transform.position = position;

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.up * 0.85f;
        body.transform.localScale = new Vector3(0.8f, 0.85f, 0.8f); // 1.7 m tall, 0.4 m radius
        body.GetComponent<Renderer>().material.color = new Color(0.55f, 0.3f, 0.3f);

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = Vector3.up * 1.85f;
        head.transform.localScale = Vector3.one * 0.35f;
        head.GetComponent<Renderer>().material.color = new Color(0.65f, 0.4f, 0.35f);

        var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visor.name = "Visor";
        DestroyImmediate(visor.GetComponent<Collider>());
        visor.transform.SetParent(root.transform, false);
        visor.transform.localPosition = new Vector3(0f, 1.87f, 0.16f);
        visor.transform.localScale = new Vector3(0.28f, 0.07f, 0.06f);
        visor.GetComponent<Renderer>().material.color = Color.black;

        root.AddComponent<Health>().max = 100f;
        var source = root.AddComponent<AudioSource>();
        source.spatialBlend = 1f;
        source.maxDistance = 60f;

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 2f;
        agent.acceleration = 20f;
        agent.angularSpeed = 360f;
        agent.stoppingDistance = 0.5f;

        return root.AddComponent<EnemyAI>();
    }
}
