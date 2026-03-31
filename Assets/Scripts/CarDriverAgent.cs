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
        Transform nextCheckpoint = track.GetNextCheckpoint(transform);
        Vector3 dirToCheckpoint = (nextCheckpoint.position - transform.position).normalized;

        sensor.AddObservation(dirToCheckpoint);
        sensor.AddObservation(transform.forward);
        sensor.AddObservation(rb.linearVelocity.magnitude);
    }

    public override void OnActionReceived(ActionBuffers actions) {
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
        float directionLookAlignment = Vector3.Dot(transform.forward, dirToCheckpoint);
        float velocityAlignment = Vector3.Dot(rb.linearVelocity.normalized, dirToCheckpoint);
        
        if (directionLookAlignment > 0 && velocityAlignment > 0) {
            AddReward(velocityAlignment * 0.01f);
        }else {
            AddReward(-0.01f);
        }

        AddReward(-0.001f);
    }

    public override void Heuristic(in ActionBuffers actionsOut) {
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
            EndEpisode();
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
