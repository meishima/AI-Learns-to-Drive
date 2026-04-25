using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class CarDriverAgent : Agent {
    [SerializeField] private Track track;
    [SerializeField] private Transform spawnPosition;

    private CarController carController;
    private Rigidbody rb;

    public override void Initialize() {
        carController = GetComponent<CarController>();
        rb = GetComponent<Rigidbody>();
    }

    private void Start() {
        track.OnCarCorrectCheckpoint += (car) => {
            if (car == transform) {
                AddReward(1f);
            }
        };
        track.OnCarWrongCheckpoint += (car) => {
            if (car == transform) {
                AddReward(-1f);
            }
        };

        track.OnLapCompleted += (car) => {
            if (car == transform) {
                AddReward(10f);
            }
        };
    }

    public override void OnEpisodeBegin() {
        transform.position = spawnPosition.position + new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
        transform.forward = spawnPosition.forward;
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
            // Provide continuous dense reward for actively moving towards the next checkpoint
            AddReward(speedTowardsCheckpoint * 0.002f);
        } else if (speedTowardsCheckpoint < -0.1f) {
            // Punish driving backwards explicitly
            AddReward(-0.002f);
        }

        // A very tiny time penalty to encourage urgency, but deliberately small so the accumulated episode sum isn't worse than full-force Crashing!
        AddReward(-0.0002f);
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
            AddReward(-1.5f);
            
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
                transform.position = new Vector3(0, -9000, 0); 
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
