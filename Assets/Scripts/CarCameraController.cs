using UnityEngine;

public class CarCameraController : MonoBehaviour {
    public Transform target;
    public Vector3 offset;
    public float followSpeed;
    public float lookSpeed;

    public void LookAtTarget() {
        Vector3 direction = target.position - transform.position;
        if (direction.sqrMagnitude > 0.001f) {
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, lookSpeed * Time.deltaTime);
        }
    }

    public void MoveToTarget() {
        Vector3 desiredPosition = target.position + target.forward * offset.z + target.up * offset.y + target.right * offset.x;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, followSpeed * Time.deltaTime);
    }

    private void FindAliveTarget() {
        CarDriverAgent[] allAgents = Object.FindObjectsByType<CarDriverAgent>(FindObjectsInactive.Exclude);
        foreach (var agent in allAgents) {
            if (agent.transform == target) continue; // Don't pick the dead one again
            
            Rigidbody agentRb = agent.GetComponent<Rigidbody>();
            // A car is 'alive' if it is actively driving (not kinematic)
            if (agentRb != null && !agentRb.isKinematic) {
                target = agent.transform;
                return;
            }
        }
    }

    private void FixedUpdate() {
        if (target == null || !target.gameObject.activeInHierarchy) {
            FindAliveTarget();
        } else {
            Rigidbody targetRb = target.GetComponent<Rigidbody>();
            if (targetRb != null && targetRb.isKinematic) {
                FindAliveTarget(); // Switch if our current car dies
            }
        }

        if (target != null) {
            LookAtTarget();
            MoveToTarget();
        }
    }
}
