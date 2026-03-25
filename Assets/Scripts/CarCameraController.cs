using UnityEngine;

public class CarCameraController : MonoBehaviour
{
    public Transform target;
    public Vector3 offset;
    public float followSpeed;
    public float lookSpeed;

    public void LookAtTarget() {
        Vector3 direction = target.position - transform.position;
        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, lookSpeed * Time.deltaTime);
    }

    public void MoveToTarget() {
        Vector3 desiredPosition = target.position + target.forward * offset.z + target.up * offset.y + target.right * offset.x;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, followSpeed * Time.deltaTime);
    }

    private void FixedUpdate() {
        LookAtTarget();
        MoveToTarget();
    }
}
