using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System;
using Unity.VisualScripting;
using Random = UnityEngine.Random;

public class MoveToGoalAgent : Agent {
    [SerializeField] private Transform targetTransform;
    [SerializeField] private Material winMaterial;
    [SerializeField] private Material loseMaterial;
    [SerializeField] private MeshRenderer floorMeshRenderer;

    public override void OnEpisodeBegin() {
        transform.localPosition = new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, -0.5f));
        targetTransform.localPosition = new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(3f, 0.5f));
    }
    public override void CollectObservations(VectorSensor sensor) {
        sensor.AddObservation(transform.localPosition);
        sensor.AddObservation(targetTransform.localPosition);
    }
    public override void OnActionReceived(ActionBuffers actions) {
        float moveX = actions.ContinuousActions[0];
        float moveZ = actions.ContinuousActions[1];

        Vector3 moveVector = new Vector3(moveX, 0, moveZ);
        float moveSpeed = 5f;
        transform.localPosition += moveVector * Time.deltaTime * moveSpeed;
    }

    public override void Heuristic(in ActionBuffers actionsOut) {
        ActionSegment<float> continuousActions = actionsOut.ContinuousActions;
        continuousActions[0] = Input.GetAxis("Horizontal");
        continuousActions[1] = Input.GetAxis("Vertical");
    }
    private void OnTriggerEnter(Collider other) {
        if (other.TryGetComponent<Goal>(out Goal goal)) {
            SetReward(1f);
            floorMeshRenderer.material = winMaterial;
            EndEpisode();
        }
        if (other.TryGetComponent<Wall>(out Wall wall)) {
            SetReward(-1f);
            floorMeshRenderer.material = loseMaterial;
            EndEpisode();
        }
    }
}
