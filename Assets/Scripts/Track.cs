using UnityEngine;
using System.Collections.Generic;

public class Track : MonoBehaviour {
    [SerializeField] private List<Transform> carList;
    private List<CheckpointSingle> checkpointList;
    private Dictionary<Transform, int> carCheckpointIndex;

    private void Awake() {
        checkpointList = new List<CheckpointSingle>(GetComponentsInChildren<CheckpointSingle>());

        foreach (CheckpointSingle checkpoint in checkpointList) {
            checkpoint.SetTrack(this);
        }

        carCheckpointIndex = new Dictionary<Transform, int>();

        foreach (Transform car in carList) {
            carCheckpointIndex[car] = 0;
        }
    }

    public void CarThroughCheckpoint(CheckpointSingle checkpoint, Transform car) {
        if (!carCheckpointIndex.ContainsKey(car)) {
            return;
        }

        int currentIndex = checkpointList.IndexOf(checkpoint);
        int expectedIndex = carCheckpointIndex[car];

        if (currentIndex == expectedIndex) {
            carCheckpointIndex[car] = (expectedIndex + 1) % checkpointList.Count;
            Debug.Log("Correct Checkpoint");
        }
        else {
            Debug.Log("Wrong Checkpoint");
        }
    }
}
