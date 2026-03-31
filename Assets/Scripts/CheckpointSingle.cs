using UnityEngine;

public class CheckpointSingle : MonoBehaviour {
    private Track track;
    private int index;

    public void SetTrack(Track track, int index) {
        this.track = track;
        this.index = index;
    }
    
    private void OnTriggerEnter(Collider collider) {
        if (collider.TryGetComponent<CarDriverAgent>(out CarDriverAgent car)) {
            track.CarThroughCheckpoint(index, car.transform);
        }
    }
}
