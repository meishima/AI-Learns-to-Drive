using UnityEngine;
using System.Collections.Generic;

public class Track : MonoBehaviour {
    public System.Action<Transform> OnCarCorrectCheckpoint;
    public System.Action<Transform> OnCarWrongCheckpoint;
    public System.Action<Transform> OnLapCompleted;
    
    private List<CheckpointSingle> checkpointList;
    private Dictionary<Transform, int> carCheckpointIndex = new Dictionary<Transform, int>();

    private void Awake() {
        checkpointList = new List<CheckpointSingle>(GetComponentsInChildren<CheckpointSingle>());

        for (int i = 0; i < checkpointList.Count; i++) {
            checkpointList[i].SetTrack(this, i);
        }
    }

    public void ResetCheckpoints(Transform car) {
        carCheckpointIndex[car] = 0;
    }

    public Transform GetNextCheckpoint(Transform car) {
        if (checkpointList == null || checkpointList.Count == 0) {
            checkpointList = new List<CheckpointSingle>(GetComponentsInChildren<CheckpointSingle>(true));
            
            if (checkpointList.Count == 0) return transform;

            for (int i = 0; i < checkpointList.Count; i++) {
                checkpointList[i].SetTrack(this, i);
            }
        }
        if (!carCheckpointIndex.TryGetValue(car, out int index)) {
            index = 0;
            carCheckpointIndex[car] = 0;
        }
        return checkpointList[index].transform;
    }

    public void CarThroughCheckpoint(int checkpointIndex, Transform car) {
        if (!carCheckpointIndex.TryGetValue(car, out int expectedIndex)) return;

        if (checkpointIndex == expectedIndex) {
            if (expectedIndex == checkpointList.Count - 1) {
                OnLapCompleted?.Invoke(car);
            }
            carCheckpointIndex[car] = (expectedIndex + 1) % checkpointList.Count;
            OnCarCorrectCheckpoint?.Invoke(car);
        } else {
            OnCarWrongCheckpoint?.Invoke(car);
        }
    }
}
