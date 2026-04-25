using UnityEngine;

public class MapRotator : MonoBehaviour
{
    public TrackGenerator generator;
    public CarDriverAgent[] agents;
    public float rotationTimeSeconds = 60f;

    private float timer = 0f;
    private bool isGenerating = false;

    void Start()
    {
        if (generator == null) generator = Object.FindAnyObjectByType<TrackGenerator>();
        if (agents == null || agents.Length == 0) agents = Object.FindObjectsByType<CarDriverAgent>(FindObjectsInactive.Include);

        // Link our Reset sequence securely to the exact termination moment of the Physics-Pausing Coroutine!
        generator.OnTrackGenerated += RepositionAllAgents;
        
        timer = rotationTimeSeconds;
        TriggerMapWipe(); // Spawn the first map instantly
    }

    void Update()
    {
        if (isGenerating) return;

        timer -= Time.deltaTime;
        
        int aliveCount = 0;
        foreach (var agent in agents) {
            if (agent == null) continue;
            var rb = agent.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) {
                aliveCount++;
            }
        }

        // If more than half of the agents crashed in under 60 seconds, or the 60s learning limit expires entirely
        if (timer <= 0f || aliveCount < (agents.Length / 2.0f)) {
            TriggerMapWipe();
            timer = rotationTimeSeconds;
        }
    }

    public void TriggerMapWipe()
    {
        isGenerating = true;

        // Freeze the agents implicitly
        foreach (var agent in agents) {
            if (agent != null) {
                var rb = agent.GetComponent<Rigidbody>();
                if (rb != null) {
                    if (!rb.isKinematic) {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                    rb.isKinematic = true;
                }
            }
        }

        // 1. Execute full map structural destruction
        generator.ClearTrack();

        // 2. Queue the asynchronous map reconstruction (Instantly freezes Time.timeScale!)
        // The agents are NOT repositioned here! They wait frozen until OnTrackGenerated fires natively.
        generator.GenerateComplexTrack();
    }

    private void RepositionAllAgents()
    {
        StartCoroutine(RepositionNextFrame());
    }

    private System.Collections.IEnumerator RepositionNextFrame()
    {
        // Explicitly wait precisely 1 hardware Physics Frame organically! 
        // This gives the Engine's Spatial Hasher native time to effortlessly flush the garbage queue and build the fresh Track colliders without manually accelerating C++ serialization overrides!
        yield return new WaitForFixedUpdate();

        foreach (var agent in agents) {
            if (agent == null) continue;

            var rb = agent.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;

            // Halt any residual inertia mathematically 
            var car = agent.GetComponent<CarController>();
            if (car != null) car.StopCompletely();

            // EndEpisode automatically redirects them back to spawnPosition via OnEpisodeBegin
            agent.EndEpisode(); 
        }

        isGenerating = false;
    }

    void OnDestroy()
    {
        // Clean up our memory linkage safely
        if (generator != null) {
            generator.OnTrackGenerated -= RepositionAllAgents;
        }
    }
}
