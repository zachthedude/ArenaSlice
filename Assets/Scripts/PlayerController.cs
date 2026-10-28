using UnityEngine;

// First-person controller. Movement is an explicit state machine (enum + switch) so every transition is one line you can point at.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public enum State { Walk, Sprint, Crouch, Slide, Air }

    [Header("Speeds (m/s)")]
    public float walkSpeed = 5f, sprintSpeed = 8f, crouchSpeed = 2.5f;

    [Header("Kinematics")]
    [Tooltip("Real g (9.81) makes a 1.2 m jump last 0.99 s and feel floaty. 20 keeps the height and cuts it to 0.69 s.")]
    public float gravity = 20f;
    public float jumpHeight = 1.2f;
    [Tooltip("Reaches sprint speed in 0.13 s and stops just as fast, so the player stops when they let go.")]
    public float groundAcceleration = 60f;
    public float airAcceleration = 12f;
    [Tooltip("Sliding friction coefficient: deceleration = mu * g * cos(slope).")]
    public float slideFriction = 0.3f;

    [Header("Forgiveness (s)")]
    public float coyoteTime = 0.1f; // about six frames at 60 fps
    public float jumpBuffer = 0.12f;

    [Header("Body (m)")]
    public float standHeight = 1.8f, crouchHeight = 1.0f, radius = 0.35f;

    public State Current { get; private set; } = State.Walk;
    public Vector3 Velocity => velocity;
    public Camera Cam { get; private set; }
    public AudioSource Audio { get; private set; }
    public Health Health { get; private set; }
    public bool Aiming { get; private set; }
    public Weapon CurrentWeapon => weapons[weaponIndex];
    public Vector3 EyePosition => Cam.transform.position;

    // How much movement opens up the weapon's spread (0 standing still, 1 at a sprint).
    public float SpreadFactor => Current switch
    {
        State.Air => 1.5f,
        State.Slide => 1f,
        _ => new Vector2(velocity.x, velocity.z).magnitude / sprintSpeed,
    };

    static readonly Vector3 HipPosition = new Vector3(0.22f, -0.2f, 0.45f);
    static readonly Vector3 AdsPosition = new Vector3(0f, -0.12f, 0.35f);

    CharacterController cc;
    Vector3 velocity, groundNormal = Vector3.up;
    Vector2 recoil;
    float yaw, pitch, stepDistance;
    float lastGrounded = -10f, lastJumpPressed = -10f;
    bool jumpConsumed;
    Weapon[] weapons;
    int weaponIndex;

    void Awake()
    {
        gameObject.layer = 2; // Ignore Raycast: the player's own shots and headroom checks skip this body
        cc = GetComponent<CharacterController>();
        cc.height = standHeight;
        cc.radius = radius;
        cc.center = Vector3.up * (standHeight / 2f);
        cc.stepOffset = 0.4f;
        cc.slopeLimit = 45f;

        Health = gameObject.AddComponent<Health>();
        Health.regenDelay = 4f;
        Health.regenPerSecond = 15f;
        Health.Died += Game.Lose;
        Health.Damaged += (amount, from) => { Feel.PlayerHit(amount); Game.ShowDamageFrom(from); };

        var camGo = new GameObject("Camera") { tag = "MainCamera" };
        camGo.transform.SetParent(transform, false);
        camGo.transform.localPosition = Vector3.up * (standHeight - 0.1f);
        Cam = camGo.AddComponent<Camera>();
        Cam.nearClipPlane = 0.03f;
        camGo.AddComponent<AudioListener>();
        Audio = camGo.AddComponent<AudioSource>();

        yaw = transform.eulerAngles.y;

        weapons = new Weapon[]
        {
            AddWeapon<HitscanWeapon>("Rifle", new Vector3(0.07f, 0.09f, 0.6f), w =>
            {
                w.automatic = true; w.roundsPerMinute = 650f; w.magazine = 30; w.reloadTime = 1.9f; w.adsZoom = 1.4f;
                w.damage = 24f; w.headMultiplier = 2f; w.falloffStart = 25f; w.falloffEnd = 60f; w.falloffMinFraction = 0.6f;
                w.hipSpread = 1.2f; w.adsSpread = 0.2f; w.movementSpread = 2.5f;
                w.recoilPitch = 0.9f; w.recoilYaw = 0.35f; w.noiseRadius = 45f;
            }),
            AddWeapon<ProjectileWeapon>("Launcher", new Vector3(0.12f, 0.12f, 0.5f), w =>
            {
                w.automatic = false; w.roundsPerMinute = 80f; w.magazine = 4; w.reloadTime = 2.6f; w.adsZoom = 1.25f;
                w.damage = 90f; w.headMultiplier = 1.5f; w.falloffStart = 40f; w.falloffEnd = 90f; w.falloffMinFraction = 0.8f;
                w.hipSpread = 0.8f; w.adsSpread = 0.15f; w.movementSpread = 1.5f;
                w.recoilPitch = 4f; w.recoilYaw = 1f; w.noiseRadius = 35f; w.muzzleVelocity = 60f;
            }),
        };
        for (int i = 1; i < weapons.Length; i++) weapons[i].gameObject.SetActive(false);
    }

    T AddWeapon<T>(string name, Vector3 size, System.Action<T> configure) where T : Weapon
    {
        var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        model.name = name;
        DestroyImmediate(model.GetComponent<Collider>());
        model.transform.SetParent(Cam.transform, false);
        model.transform.localPosition = HipPosition;
        model.transform.localScale = size;
        model.GetComponent<Renderer>().material.color = new Color(0.18f, 0.18f, 0.2f);

        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(model.transform, false);
        muzzle.localPosition = new Vector3(0f, 0f, 0.5f); // front face of the unit cube

        var weapon = model.AddComponent<T>();
        weapon.displayName = name;
        configure(weapon);
        weapon.Init(this, muzzle);
        return weapon;
    }

    void Update()
    {
        if (!Game.Playing) return;
        float dt = Time.deltaTime;
        Look(dt);
        Move(dt);
        UpdateBody(dt);
        HandleWeapons(dt);
    }

    public void AddRecoil(float pitchDegrees, float yawDegrees) => recoil += new Vector2(pitchDegrees, yawDegrees);

    void Look(float dt)
    {
        var s = GameSettings.Current;
        Vector2 d = Controls.Look * (Aiming ? s.adsSensitivity : s.sensitivity);
        yaw += d.x;
        pitch = Mathf.Clamp(pitch - d.y, -89f, 89f);
        recoil *= Mathf.Exp(-8f * dt); // recoil springs back exponentially

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Cam.transform.localRotation = Quaternion.Euler(pitch - recoil.x, recoil.y, 0f) * Feel.ShakeRotation;

        // The setting is horizontal FOV (what players expect); Unity's camera wants vertical.
        float horizontal = s.fov / (Aiming ? CurrentWeapon.adsZoom : 1f) + Feel.FovKick;
        float target = Camera.HorizontalToVerticalFieldOfView(horizontal, Cam.aspect);
        Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, target, 1f - Mathf.Exp(-15f * dt));
    }

    State NextState(bool grounded, Vector2 input, float speed)
    {
        if (!grounded) return State.Air;
        bool wantsSprint = Controls.Sprint && input.y > 0f && !Controls.Fire && !Controls.Aim;

        switch (Current)
        {
            case State.Air:
                // Landing with crouch held and real speed goes straight into a slide.
                if (Controls.CrouchHeld) return speed > walkSpeed ? State.Slide : State.Crouch;
                return wantsSprint ? State.Sprint : State.Walk;
            case State.Sprint:
                // A slide can only start from a sprint.
                if (Controls.CrouchDown) return speed > walkSpeed ? State.Slide : State.Crouch;
                return wantsSprint ? State.Sprint : State.Walk;
            case State.Slide:
                if (Controls.CrouchHeld && speed > crouchSpeed) return State.Slide;
                return !Controls.CrouchHeld && CanStand() ? State.Walk : State.Crouch;
            case State.Crouch:
                if (Controls.CrouchHeld || !CanStand()) return State.Crouch;
                return wantsSprint ? State.Sprint : State.Walk;
            default: // Walk
                if (Controls.CrouchDown) return State.Crouch;
                return wantsSprint ? State.Sprint : State.Walk;
        }
    }

    float TargetSpeed => Current switch
    {
        State.Sprint => sprintSpeed,
        State.Crouch => crouchSpeed,
        _ => walkSpeed,
    };

    void Move(float dt)
    {
        bool grounded = cc.isGrounded;
        if (grounded)
        {
            lastGrounded = Time.time;
            if (velocity.y <= 0f) jumpConsumed = false;
        }
        else groundNormal = Vector3.up;
        if (Controls.Jump) lastJumpPressed = Time.time;

        Vector2 input = Controls.Move;
        Vector3 wish = Vector3.ClampMagnitude(transform.right * input.x + transform.forward * input.y, 1f);
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);

        Current = NextState(grounded, input, flat.magnitude);
        if (grounded && velocity.y < 0f) velocity.y = -2f; // keeps the controller pressed onto slopes and steps
        TryJump();

        switch (Current)
        {
            case State.Air:
                // No air drag: momentum is kept, input only steers it.
                if (wish != Vector3.zero) flat = Vector3.MoveTowards(flat, wish * Mathf.Max(flat.magnitude, walkSpeed), airAcceleration * dt);
                velocity.y -= gravity * dt;
                break;
            case State.Slide:
                // Gravity's component along the slope (g sin theta) speeds a downhill slide up; friction (mu g cos theta) slows it.
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down * gravity, groundNormal);
                flat += new Vector3(downhill.x, 0f, downhill.z) * dt;
                flat = Vector3.MoveTowards(flat, Vector3.zero, slideFriction * gravity * groundNormal.y * dt);
                break;
            default:
                flat = Vector3.MoveTowards(flat, wish * TargetSpeed, groundAcceleration * dt);
                break;
        }
        velocity.x = flat.x;
        velocity.z = flat.z;

        cc.Move(velocity * dt);

        if (!grounded && cc.isGrounded && velocity.y < -7f)
        {
            EnemyAI.Hear(transform.position, 10f); // a hard landing is loud
            Audio.PlayOneShot(Sfx.Step, 0.8f);
        }
        Footsteps(grounded, flat.magnitude * dt);
    }

    void TryJump()
    {
        bool buffered = Time.time - lastJumpPressed <= jumpBuffer; // pressed slightly before landing still counts
        bool coyote = Time.time - lastGrounded <= coyoteTime;      // just walked off a ledge still counts
        if (!buffered || !coyote || jumpConsumed) return;
        if (cc.height < standHeight - 0.05f && !CanStand()) return;

        velocity.y = Kinematics.TakeoffSpeed(gravity, jumpHeight);
        jumpConsumed = true;
        lastJumpPressed = -10f;
        Current = State.Air;
    }

    void Footsteps(bool grounded, float distance)
    {
        if (!grounded || Current == State.Crouch) return; // crouching is silent
        stepDistance += distance;
        if (stepDistance < 2f) return;
        stepDistance = 0f;
        EnemyAI.Hear(transform.position, Current == State.Sprint ? 14f : Current == State.Slide ? 8f : 5f);
        Audio.PlayOneShot(Sfx.Step, Current == State.Sprint ? 0.5f : 0.25f);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y > 0.7f) { groundNormal = hit.normal; return; }
        // Walls and ceilings: remove the part of the velocity pointing into the surface (inelastic collision),
        // otherwise a slide into a wall would keep its speed stored up.
        float into = Vector3.Dot(velocity, hit.normal);
        if (into < 0f) velocity -= hit.normal * into;
    }

    bool CanStand()
    {
        float r = radius * 0.9f;
        Vector3 p = transform.position;
        return !Physics.CheckCapsule(p + Vector3.up * (r + 0.1f), p + Vector3.up * (standHeight - r), r,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    void UpdateBody(float dt)
    {
        float target = Current == State.Crouch || Current == State.Slide ? crouchHeight : standHeight;
        cc.height = Mathf.MoveTowards(cc.height, target, 6f * dt);
        cc.center = Vector3.up * (cc.height / 2f);
        Cam.transform.localPosition = Vector3.up * (cc.height - 0.1f);
    }

    void HandleWeapons(float dt)
    {
        int slot = Controls.WeaponSlot;
        float scroll = Controls.Scroll;
        if (scroll != 0f) slot = (weaponIndex + (scroll > 0f ? 1 : weapons.Length - 1)) % weapons.Length;
        if (slot >= 0 && slot < weapons.Length && slot != weaponIndex)
        {
            CurrentWeapon.gameObject.SetActive(false);
            weaponIndex = slot;
            CurrentWeapon.gameObject.SetActive(true);
        }

        Aiming = Controls.Aim && Current != State.Sprint;
        if (Controls.Reload) CurrentWeapon.StartReload();
        CurrentWeapon.Tick(Controls.Fire, Controls.FireDown, Aiming);

        Transform gun = CurrentWeapon.transform;
        gun.localPosition = Vector3.Lerp(gun.localPosition, Aiming ? AdsPosition : HipPosition, 1f - Mathf.Exp(-20f * dt));
    }
}
