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
    private const float ALPHA_WRONG_CHECKPOINT   = -0.5f;  // barely cares
    private const float ALPHA_LAP_REWARD         = 30.0f;
    private const float ALPHA_WALL_PENALTY       = -0.5f;  // wall scrapes = acceptable
    private const float ALPHA_CAR_COLLISION      = -2.0f;  // bumper-car mentality
    private const float ALPHA_SPEED_SCALE        =  0.012f;// big per-step speed hunger
    private const float ALPHA_REVERSE_PENALTY    = -0.01f; // mild — just turn around and go
    private const float ALPHA_TIME_PENALTY       = -0.0003f;
    private const float ALPHA_OVERTAKE_REWARD    = 10.0f;  // overtaking is the whole game

    // ─────────────────────────────────────────────────────────
    // BETA — Passive, Slow & Precise
    //   Prioritizes clean, contact-free driving. Actively fears
    //   walls and other cars. Will slow down to avoid any risk.
    //   Rewards come from doing the right thing, not raw speed.
    // ─────────────────────────────────────────────────────────
    private const float BETA_CHECKPOINT_REWARD   =  7.0f;  // checkpoints are everything
    private const float BETA_WRONG_CHECKPOINT    = -5.0f;  // hates going the wrong way
    private const float BETA_LAP_REWARD          = 20.0f;
    private const float BETA_WALL_PENALTY        = -8.0f;  // wall = catastrophic
    private const float BETA_CAR_COLLISION       = -20.0f; // contact is forbidden
    private const float BETA_SPEED_SCALE         =  0.003f;// small speed bonus — not the priority
    private const float BETA_REVERSE_PENALTY     = -0.08f; // strongly dislikes going backwards
    private const float BETA_TIME_PENALTY        = -0.003f;// still penalized for being too slow
    private const float BETA_OVERTAKE_REWARD     =  3.0f;  // passing is nice but not the goal

    // ─────────────────────────────────────────────────────────
    // Shared overtake tracking
    // ─────────────────────────────────────────────────────────
    private static readonly Dictionary<Transform, int> s_progressRegistry
        = new Dictionary<Transform, int>();

    private int myLapsCompleted = 0;
    private int myLastRank      = 0;

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

        foreach (var r in GetComponentsInChildren<Renderer>())
            r.material.color = carColor;

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
        int row = myIndex / 4;
        int col = myIndex % 4;

        float scaleMult = (trackGenerator != null) ? trackGenerator.globalScaleMultiplier : 1f;
        float offsetX   = (-4.5f + (col * 3f)) * scaleMult;
        float offsetZ   = ((15f * scaleMult) - 3f) - (row * 5.5f);

        Transform spawnRef = trackGenerator != null ? trackGenerator.GetSpawnTransform() : transform;
        transform.position = spawnRef.position + spawnRef.TransformDirection(new Vector3(offsetX, 0, offsetZ));
        transform.forward  = spawnRef.forward;

        track.ResetCheckpoints(transform);
        carController.StopCompletely();

        myLapsCompleted = 0;
        myLastRank      = 0;
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

        if (speedTowardCP > 0.5f) {
            float scale = team == AgentTeam.Alpha ? ALPHA_SPEED_SCALE : BETA_SPEED_SCALE;
            AddReward(speedTowardCP * scale);
        } else if (speedTowardCP < -0.1f) {
            AddReward(team == AgentTeam.Alpha ? ALPHA_REVERSE_PENALTY : BETA_REVERSE_PENALTY);
        }

        AddReward(team == AgentTeam.Alpha ? ALPHA_TIME_PENALTY : BETA_TIME_PENALTY);

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
