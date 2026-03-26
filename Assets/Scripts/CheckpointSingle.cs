using UnityEngine;

public class CheckpointSingle : MonoBehaviour {
    private  Track track;

    public void SetTrack(Track track) {
        this.track = track;
    }
    
    private void OnTriggerEnter(Collider collider) {
        if (collider.TryGetComponent<CarController>(out CarController car)) {
            track.CarThroughCheckpoint(this, collider.transform);
        }
    }
}
