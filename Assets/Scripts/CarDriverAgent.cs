using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────
// Set the "Behavior Name" in Behavior Parameters to match:
//   Alpha → "CarDriverAgent_Alpha"
//   Beta  → "CarDriverAgent_Beta"
// ─────────────────────────────────────────────────────────────
public enum AgentTeam { Alpha, Beta }

public class CarDriverAgent : Agent {
    [SerializeField] private Track track;

    [Header("Team Assignment")]
    [Tooltip("Alpha = Aggressive & Fast. Beta = Passive, Slow & Precise.")]
    public AgentTeam team = AgentTeam.Alpha;

    // ─────────────────────────────────────────────────────────
    // ALPHA — Aggressive & Fast
    //   Drives as fast as possible. Accepts wall scrapes and
    //   car contact as a cost of doing business. Lives for
    //   overtakes. Barely slows for anything.
    // ─────────────────────────────────────────────────────────
    private const float ALPHA_CHECKPOINT_REWARD  =  3.0f;
    private const float ALPHA_WRONG_CHECKPOINT   = -2.0f;  // strong deterrent for wrong direction
    private const float ALPHA_LAP_REWARD         = 30.0f;
    private const float ALPHA_WALL_PENALTY       = -25.0f; // raised at 2M steps — agents know to drive, walls now heavily punished
    private const float ALPHA_CAR_COLLISION      = -9.0f;  // costly but half an overtake — aggressive, not reckless
    private const float ALPHA_SPEED_SCALE        =  0.025f;// raised: big per-step speed hunger
    private const float ALPHA_REVERSE_PENALTY    = -0.15f; // 15× stronger — wrong way = bad
    private const float ALPHA_TIME_PENALTY       = -0.002f;// raised: constant pressure to go fast
    private const float ALPHA_OVERTAKE_REWARD    = 30.0f;  // overtaking is the whole game
    private const float ALPHA_SPIN_PENALTY       = -0.06f; // per rad/s above threshold — teaches braking before corners

    // Wrong-way timeout: end episode if going backwards for too many consecutive steps
    private const int   WRONG_WAY_TIMEOUT        = 150;    // raised: was 60, gave too little time to recover from spawn
    private const int   SPAWN_GRACE_STEPS        = 100;    // first N steps after spawn are timeout-immune

    // ─────────────────────────────────────────────────────────
    // BETA — Passive, Slow & Precise
    //   Prioritizes clean, contact-free driving. Actively fears
    //   walls and other cars. Will slow down to avoid any risk.
    //   Rewards come from doing the right thing, not raw speed.
    // ─────────────────────────────────────────────────────────
    private const float BETA_CHECKPOINT_REWARD   = 25.0f;  // large: first discovery must be an unmissable signal
    private const float BETA_WRONG_CHECKPOINT    = -3.0f;  // reduced: less brutal during exploration
    private const float BETA_LAP_REWARD          = 20.0f;
    private const float BETA_WALL_PENALTY        = -6.0f;  // raised at 2M steps — Beta is finding checkpoints, walls should deter now
    private const float BETA_CAR_COLLISION       = -12.0f; // still painful but not episode-ending by itself
    private const float BETA_SPEED_SCALE         =  0.006f;// modest speed bonus
    private const float BETA_REVERSE_PENALTY     = -0.12f; // reduced: still penalised, not spiral-inducing
    private const float BETA_TIME_PENALTY        = -0.001f;
    private const float BETA_APPROACH_SCALE      =  0.03f; // continuous reward for getting physically closer to checkpoint
    private const float BETA_SPIN_PENALTY        = -0.10f; // stronger: precision driver must NOT spin out
    private const float BETA_OVERTAKE_REWARD     =  3.0f;  // passing is nice but not the goal

    // ─────────────────────────────────────────────────────────
    // Shared spawn shuffle — guarantees every car gets a unique slot
    // ─────────────────────────────────────────────────────────
    private static readonly int[] s_spawnSlots = { 0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15 };

    private static void ShuffleSpawnSlots() {
        for (int i = s_spawnSlots.Length - 1; i > 0; i--) {
            int j = UnityEngine.Random.Range(0, i + 1);
            int tmp      = s_spawnSlots[i];
            s_spawnSlots[i] = s_spawnSlots[j];
            s_spawnSlots[j] = tmp;
        }
    }

    // ─────────────────────────────────────────────────────────
    // Shared overtake tracking
    // ─────────────────────────────────────────────────────────
    private static readonly Dictionary<Transform, int> s_progressRegistry
        = new Dictionary<Transform, int>();

    private int   myLapsCompleted         = 0;
    private int   myLastRank              = 0;
    private int   m_wrongWaySteps         = 0;
    private int   m_spawnSteps            = 0;
    private float m_prevDistToCheckpoint  = 0f; // for Beta continuous approach reward

    private CarController carController;
    private Rigidbody rb;
    private int myIndex = -1;
    private TrackGenerator trackGenerator;

    // ─────────────────────────────────────────────────────────
    public override void Initialize() {
        carController  = GetComponent<CarController>();
        rb             = GetComponent<Rigidbody>();
        trackGenerator = Object.FindAnyObjectByType<TrackGenerator>();
        if (track == null) track = Object.FindAnyObjectByType<Track>();

        CarDriverAgent[] allAgents = Object.FindObjectsByType<CarDriverAgent>(FindObjectsInactive.Include);
        System.Array.Sort(allAgents, (a, b) =>
            a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        myIndex = System.Array.IndexOf(allAgents, this);

        s_progressRegistry[transform] = 0;

        if (myIndex == 0 && allAgents.Length > 16)
            Debug.LogWarning("More than 16 cars detected! They might spill off the runway.");
    }

    private void Start() {
        // Each car gets its own color within the team palette
        // Alpha: warm tones (reds, oranges, yellows, pinks)
        Color[] alphaColors = {
            new Color(1.00f, 0.10f, 0.10f), // 0 — bright red
            new Color(1.00f, 0.35f, 0.00f), // 1 — deep orange
            new Color(1.00f, 0.65f, 0.00f), // 2 — amber
            new Color(1.00f, 0.90f, 0.00f), // 3 — yellow
            new Color(1.00f, 0.00f, 0.50f), // 4 — hot pink
            new Color(0.85f, 0.00f, 0.10f), // 5 — crimson
            new Color(1.00f, 0.00f, 0.85f), // 6 — magenta
            new Color(0.80f, 0.40f, 0.00f), // 7 — burnt orange
        };
        // Beta: cool tones (blues, teals, indigos, cyans)
        Color[] betaColors = {
            new Color(0.10f, 0.45f, 1.00f), // 0 — royal blue
            new Color(0.00f, 0.80f, 0.80f), // 1 — teal
            new Color(0.00f, 0.85f, 1.00f), // 2 — cyan
            new Color(0.35f, 0.60f, 0.90f), // 3 — steel blue
            new Color(0.45f, 0.00f, 1.00f), // 4 — indigo
            new Color(0.55f, 0.75f, 1.00f), // 5 — sky blue
            new Color(0.20f, 0.20f, 0.85f), // 6 — deep blue
            new Color(0.75f, 0.90f, 1.00f), // 7 — ice blue
        };

        Color carColor;
        if (team == AgentTeam.Alpha) {
            int slot = Mathf.Clamp(myIndex, 0, alphaColors.Length - 1);
            carColor = alphaColors[slot];
        } else {
            // Beta cars are slots 8-15, so offset into the beta array
            int slot = Mathf.Clamp(myIndex - alphaColors.Length, 0, betaColors.Length - 1);
            carColor = betaColors[slot];
        }

        // Iterate every renderer's material slots and color only the hull paint
        // material (identified by name containing "16"). If nothing matches,
        // agent 0 will log all material names to the Console so you can adjust.
        bool anyColored = false;
        foreach (var r in GetComponentsInChildren<Renderer>()) {
            Material[] mats = r.materials; // returns per-instance copies
            bool changed = false;
            for (int mi = 0; mi < mats.Length; mi++) {
                if (myIndex == 0)
                    Debug.Log($"[CarColor] renderer={r.name}  mat[{mi}]={mats[mi].name}");

                if (mats[mi].name.IndexOf("16", System.StringComparison.OrdinalIgnoreCase) >= 0) {
                    mats[mi].color = carColor;
                    changed = true;
                    anyColored = true;
                }
            }
            if (changed) r.materials = mats; // write instances back
        }

        if (!anyColored)
            Debug.LogWarning($"[CarColor] Agent {myIndex}: no material containing '16' found. Check Console logs above for material names.");

        // ── Track events ──────────────────────────────────────
        track.OnCarCorrectCheckpoint += (car) => {
            if (car != transform) return;
            AddReward(team == AgentTeam.Alpha ? ALPHA_CHECKPOINT_REWARD : BETA_CHECKPOINT_REWARD);
        };

        track.OnCarWrongCheckpoint += (car) => {
            if (car != transform) return;
            AddReward(team == AgentTeam.Alpha ? ALPHA_WRONG_CHECKPOINT : BETA_WRONG_CHECKPOINT);
        };

        track.OnLapCompleted += (car) => {
            if (car != transform) return;
            myLapsCompleted++;
            AddReward(team == AgentTeam.Alpha ? ALPHA_LAP_REWARD : BETA_LAP_REWARD);
        };
    }

    public override void OnEpisodeBegin() {
        // Deterministic grid — no shared mutable state, no race conditions.
        // Teams are interleaved: agent indices are mapped so Alphas (0-7) and
        // Betas (8-15) alternate across columns rather than occupying separate rows.
        //   slot = even spread: 0→0, 1→2, 2→4 ... 7→14, 8→1, 9→3 ... 15→15
        int slot = (myIndex < 8) ? myIndex * 2 : (myIndex - 8) * 2 + 1;
        int row  = slot / 4;
        int col  = slot % 4;

        float scaleMult = (trackGenerator != null) ? trackGenerator.globalScaleMultiplier : 1f;
        float offsetX   = (-4.5f + (col * 3f)) * scaleMult;
        float offsetZ   = ((15f * scaleMult) - 3f) - (row * 5.5f);

        Transform spawnRef = trackGenerator != null ? trackGenerator.GetSpawnTransform() : transform;
        transform.position = spawnRef.position + spawnRef.TransformDirection(new Vector3(offsetX, 0, offsetZ));
        transform.forward  = spawnRef.forward;

        track.ResetCheckpoints(transform);
        carController.StopCompletely();

        myLapsCompleted         = 0;
        myLastRank              = 0;
        m_wrongWaySteps         = 0;
        m_spawnSteps            = 0;
        m_prevDistToCheckpoint  = -1f; // -1 signals "not initialised yet"
        s_progressRegistry[transform] = 0;
    }

    public override void CollectObservations(VectorSensor sensor) {
        if (rb != null && rb.isKinematic) {
            sensor.AddObservation(Vector3.forward); // 3
            sensor.AddObservation(Vector3.forward); // 3
            sensor.AddObservation(0f);              // 1
            return;
        }

        Transform nextCheckpoint = track.GetNextCheckpoint(transform);
        Vector3 dirToCheckpoint  = (nextCheckpoint.position - transform.position).normalized;

        sensor.AddObservation(dirToCheckpoint);             // 3
        sensor.AddObservation(transform.forward);           // 3
        sensor.AddObservation(rb.linearVelocity.magnitude); // 1
        // Total: 7
    }

    public override void OnActionReceived(ActionBuffers actions) {
        if (rb != null && rb.isKinematic) return;

        float verticalInput   = 0f;
        float horizontalInput = 0f;

        switch (actions.DiscreteActions[0]) {
            case 1: verticalInput   =  1f; break;
            case 2: verticalInput   = -1f; break;
        }
        switch (actions.DiscreteActions[1]) {
            case 1: horizontalInput =  1f; break;
            case 2: horizontalInput = -1f; break;
        }

        bool isBraking = actions.DiscreteActions[2] == 1;
        carController.SetInput(horizontalInput, verticalInput, isBraking);

        // ── Per-step rewards ──────────────────────────────────
        Transform nextCheckpoint = track.GetNextCheckpoint(transform);
        Vector3 dirToCheckpoint  = (nextCheckpoint.position - transform.position).normalized;
        float speedTowardCP      = Vector3.Dot(rb.linearVelocity, dirToCheckpoint);

        // Increment grace period counter — timeouts are frozen for first SPAWN_GRACE_STEPS steps
        m_spawnSteps++;
        bool inGrace = m_spawnSteps < SPAWN_GRACE_STEPS;

        if (team == AgentTeam.Alpha) {
            // Alpha: quadratic speed reward — going fast is exponentially better
            if (speedTowardCP > 0.5f) {
                AddReward(speedTowardCP * speedTowardCP * ALPHA_SPEED_SCALE);
                m_wrongWaySteps = 0;
            } else if (!inGrace && speedTowardCP < -0.1f) {
                AddReward(ALPHA_REVERSE_PENALTY);
                m_wrongWaySteps++;
                if (m_wrongWaySteps >= WRONG_WAY_TIMEOUT) {
                    m_wrongWaySteps = 0;
                    EndEpisode();
                    return;
                }
            } else {
                m_wrongWaySteps = 0;
            }
        } else {
            // Beta: reward physical progress toward checkpoint each step.
            // This fires even for slow / indirect movement — no checkpoint needed.
            float distToCP = (nextCheckpoint.position - transform.position).magnitude;
            if (m_prevDistToCheckpoint >= 0f) {
                float approach = m_prevDistToCheckpoint - distToCP; // positive = getting closer
                if (approach > 0f)
                    AddReward(approach * BETA_APPROACH_SCALE);
            }
            m_prevDistToCheckpoint = distToCP;

            // Still penalise sustained reverse after grace period
            if (!inGrace && speedTowardCP < -0.1f) {
                AddReward(BETA_REVERSE_PENALTY);
                m_wrongWaySteps++;
                if (m_wrongWaySteps >= WRONG_WAY_TIMEOUT) {
                    m_wrongWaySteps = 0;
                    EndEpisode();
                    return;
                }
            } else {
                m_wrongWaySteps = 0;
            }
        }

        AddReward(team == AgentTeam.Alpha ? ALPHA_TIME_PENALTY : BETA_TIME_PENALTY);

        // ── Spin penalty (shared) — discourages entering corners too fast ──
        // yaw rate > 1.5 rad/s = car is starting to lose control in a turn
        float yawRate = Mathf.Abs(rb.angularVelocity.y);
        if (yawRate > 1.5f) {
            float scale = team == AgentTeam.Alpha ? ALPHA_SPIN_PENALTY : BETA_SPIN_PENALTY;
            AddReward((yawRate - 1.5f) * scale); // scales with severity, 0 below threshold
        }

        // ── Overtake detection ────────────────────────────────
        int checkpointCount   = track.GetCheckpointCount();
        int currentCheckpoint = track.GetCheckpointIndex(transform);
        int myProgress = myLapsCompleted * Mathf.Max(checkpointCount, 1) + currentCheckpoint;
        s_progressRegistry[transform] = myProgress;

        int currentRank = 0;
        foreach (var kvp in s_progressRegistry) {
            if (kvp.Key == transform || kvp.Key == null) continue;
            if (myProgress > kvp.Value) currentRank++;
        }

        int overtakeCount = currentRank - myLastRank;
        if (overtakeCount > 0) {
            float overtakeReward = team == AgentTeam.Alpha ? ALPHA_OVERTAKE_REWARD : BETA_OVERTAKE_REWARD;
            AddReward(overtakeReward * overtakeCount);
        }
        myLastRank = currentRank;
    }

    public override void Heuristic(in ActionBuffers actionsOut) {
        if (rb != null && rb.isKinematic) return;
        var d = actionsOut.DiscreteActions;

        if (Input.GetKey(KeyCode.W))      d[0] = 1;
        else if (Input.GetKey(KeyCode.S)) d[0] = 2;
        else                              d[0] = 0;

        if (Input.GetKey(KeyCode.D))      d[1] = 1;
        else if (Input.GetKey(KeyCode.A)) d[1] = 2;
        else                              d[1] = 0;

        d[2] = Input.GetKey(KeyCode.Space) ? 1 : 0;
    }

    private void OnTriggerEnter(Collider collision) {
        if (!collision.CompareTag("Wall")) return;

        AddReward(team == AgentTeam.Alpha ? ALPHA_WALL_PENALTY : BETA_WALL_PENALTY);

        MapRotator rotator = Object.FindAnyObjectByType<MapRotator>();
        if (rotator != null) {
            if (rb != null) {
                if (!rb.isKinematic) {
                    rb.linearVelocity  = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
            }
            transform.position = new Vector3(0, 20, 0);
        } else {
            gameObject.SetActive(false);
            TrackGenerator generator = Object.FindAnyObjectByType<TrackGenerator>();
            if (generator != null) {
                generator.ClearTrack();
                generator.GenerateComplexTrack();
            } else {
                EndEpisode();
            }
        }
    }

    private void OnCollisionEnter(Collision collision) {
        if (!collision.gameObject.CompareTag("Car")) return;
        AddReward(team == AgentTeam.Alpha ? ALPHA_CAR_COLLISION : BETA_CAR_COLLISION);
    }

    private void OnDrawGizmos() {
        if (track == null) return;
        Transform nextCheckpoint = track.GetNextCheckpoint(transform);
        if (nextCheckpoint == null) return;

        Gizmos.color = team == AgentTeam.Alpha ? Color.red : Color.cyan;
        Gizmos.DrawLine(transform.position, nextCheckpoint.position);
        Gizmos.DrawWireSphere(nextCheckpoint.position, 2f);

#if UNITY_EDITOR
        UnityEditor.Handles.color = Gizmos.color;
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 4f,
            team == AgentTeam.Alpha ? "⚡ ALPHA" : "🎯 BETA"
        );
#endif
    }
}
