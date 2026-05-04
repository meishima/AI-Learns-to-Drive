using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class CarDriverAgent : Agent {
    [SerializeField] private Track track;

    private CarController carController;
    private Rigidbody rb;

    private int myIndex = -1;
    private TrackGenerator trackGenerator;

    public override void Initialize() {
        carController = GetComponent<CarController>();
        rb = GetComponent<Rigidbody>();
        trackGenerator = Object.FindAnyObjectByType<TrackGenerator>();
        if (track == null) track = Object.FindAnyObjectByType<Track>();

        CarDriverAgent[] allAgents = Object.FindObjectsByType<CarDriverAgent>(FindObjectsInactive.Include);
        System.Array.Sort(allAgents, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        
        myIndex = System.Array.IndexOf(allAgents, this);
        
        if (myIndex == 0 && allAgents.Length > 16) {
            Debug.LogWarning("More than 16 cars detected! They might spill off the runway.");
        }
    }

    private void Start() {
        track.OnCarCorrectCheckpoint += (car) => {
            if (car == transform) {
                AddReward(5f); // Made checkpoints extremely valuable so they prioritize track position
            }
        };
        track.OnCarWrongCheckpoint += (car) => {
            if (car == transform) {
                AddReward(-2f);
            }
        };

        track.OnLapCompleted += (car) => {
            if (car == transform) {
                AddReward(20f);
            }
        };
    }

    public override void OnEpisodeBegin() {
        int row = myIndex / 4;
        int col = myIndex % 4;

        float scaleMult = (trackGenerator != null) ? trackGenerator.globalScaleMultiplier : 1f;

        // 4 lanes to support 16 cars side-by-side cleanly
        float offsetX = (-4.5f + (col * 3f)) * scaleMult; 
        
        // Pin the front row just behind the start line, and space them backwards by exactly 5.5 units for a tight, realistic grid
        float startZ = (15f * scaleMult) - 3f; 
        float spacingZ = 5.5f; 
        
        float offsetZ = startZ - (row * spacingZ);

        Vector3 localOffset = new Vector3(offsetX, 0, offsetZ);
        
        Transform spawnRef = trackGenerator != null ? trackGenerator.GetSpawnTransform() : transform;

        transform.position = spawnRef.position + spawnRef.TransformDirection(localOffset);
        transform.forward = spawnRef.forward;
        track.ResetCheckpoints(transform);
        carController.StopCompletely();
    }

    public override void CollectObservations(VectorSensor sensor) {
        if (rb != null && rb.isKinematic) {
            sensor.AddObservation(Vector3.forward);
            sensor.AddObservation(Vector3.forward);
            sensor.AddObservation(0f);
            return;
        }

        Transform nextCheckpoint = track.GetNextCheckpoint(transform);
        Vector3 dirToCheckpoint = (nextCheckpoint.position - transform.position).normalized;

        sensor.AddObservation(dirToCheckpoint);
        sensor.AddObservation(transform.forward);
        sensor.AddObservation(rb.linearVelocity.magnitude);
    }

    public override void OnActionReceived(ActionBuffers actions) {
        if (rb != null && rb.isKinematic) return;

        float horizontalInput = 0f;
        float verticalInput = 0f;
        bool isBraking = false;

        switch (actions.DiscreteActions[0]) {
            case 0: verticalInput = 0f; break;
            case 1: verticalInput = 1f; break;
            case 2: verticalInput = -1f; break;
        }
        switch (actions.DiscreteActions[1]) {
            case 0: horizontalInput = 0f; break;
            case 1: horizontalInput = 1f; break;
            case 2: horizontalInput = -1f; break;
        }

        isBraking = actions.DiscreteActions[2] == 1;

        carController.SetInput(horizontalInput, verticalInput, isBraking);

        Transform nextCheckpoint = track.GetNextCheckpoint(transform);
        Vector3 dirToCheckpoint = (nextCheckpoint.position - transform.position).normalized;
        
        // Calculate raw velocity heading towards the target explicitly
        float speedTowardsCheckpoint = Vector3.Dot(rb.linearVelocity, dirToCheckpoint);
        
        if (speedTowardsCheckpoint > 0.5f) {
            // Reduced slightly so it doesn't completely mathematically overshadow the crash penalties!
            AddReward(speedTowardsCheckpoint * 0.005f);
        } else if (speedTowardsCheckpoint < -0.1f) {
            // Punish driving backwards explicitly
            AddReward(-0.01f);
        }

        // Increased time penalty to actively force urgency
        AddReward(-0.001f);
    }

    public override void Heuristic(in ActionBuffers actionsOut) {
        if (rb != null && rb.isKinematic) return;

        var discreteActions = actionsOut.DiscreteActions;

        if (Input.GetKey(KeyCode.W)) discreteActions[0] = 1;
        else if (Input.GetKey(KeyCode.S)) discreteActions[0] = 2;
        else discreteActions[0] = 0;

        if (Input.GetKey(KeyCode.D)) discreteActions[1] = 1;
        else if (Input.GetKey(KeyCode.A)) discreteActions[1] = 2;
        else discreteActions[1] = 0;
        discreteActions[2] = Input.GetKey(KeyCode.Space) ? 1 : 0;
    }

    private void OnTriggerEnter(Collider collision) {
        if (collision.CompareTag("Wall")) {
            AddReward(-2f); 
            
            MapRotator rotator = Object.FindAnyObjectByType<MapRotator>();
            if (rotator != null) {
                // Freeze exclusively to avoid ML-Agent OnDisable hierarchy warnings!
                if (rb != null) {
                    if (!rb.isKinematic) {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                    rb.isKinematic = true;
                }

                // Optionally drop them out of the sky conceptually to clear the active visual track area invisibly
                transform.position = new Vector3(0, 20, 0); 
            } else {
                // Completely standalone testing loop: Autonomously queue the Track rebuild since there is no Supervisor script!
                gameObject.SetActive(false);
                TrackGenerator generator = Object.FindAnyObjectByType<TrackGenerator>();
                if (generator != null) {
                    generator.ClearTrack();
                    generator.GenerateComplexTrack();
                } else {
                    EndEpisode(); // Barebones generic respawn
                }
            }
        }
    }

    private void OnCollisionEnter(Collision collision) {
        // Punish cars for physically crashing into each other so they learn to race cleanly
        if (collision.gameObject.CompareTag("Car")) {
            AddReward(-10f); // Massive punishment to force them to respect other cars
        }
    }

    private void OnDrawGizmos() {
        if (track != null) {
            Transform nextCheckpoint = track.GetNextCheckpoint(transform);
            
            if (nextCheckpoint != null) {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, nextCheckpoint.position);
                Gizmos.DrawWireSphere(nextCheckpoint.position, 2f);
            }
        }
    }
}
