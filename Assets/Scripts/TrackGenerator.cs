using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class TrackGenerator : MonoBehaviour
{
    public GameObject[] trackPrefabs;
    public int trackLength = 20;
    
    [Tooltip("Enable to completely randomize the target track length on generation.")]
    public bool randomizeTrackLength = false;
    public int minRandomLength = 10;
    public int maxRandomLength = 30;

    public Track track;
    
    private Transform lastExitPoint;
    private List<GameObject> straightPrefabs = new List<GameObject>();
    private List<GameObject> turnPrefabs = new List<GameObject>();

    class PieceTemplate {
        public GameObject prefab;
        public Vector3 exitOffset;
        public float exitAngle;
    }

    struct PieceDef {
        public GameObject prefab;
        public bool isRightTurn;
    }

    public GameObject startPrefab; 
    public System.Action OnTrackGenerated;
    
    private Dictionary<PieceDef, PieceTemplate> templates = new Dictionary<PieceDef, PieceTemplate>();
    private List<GameObject> activePieces = new List<GameObject>();

    void Awake() {
        if (track == null) track = GetComponentInParent<Track>();

        if (startPrefab != null) {
            ExtractTemplate(startPrefab, false);
        }

        foreach (var p in trackPrefabs) {
            if (p == startPrefab) continue; // Skip if it's already defined

            if (p.name.ToLower().Contains("turn")) turnPrefabs.Add(p);
            else straightPrefabs.Add(p);
            
            ExtractTemplate(p, false);
            if (p.name.ToLower().Contains("turn")) {
                ExtractTemplate(p, true);
            }
        }

        if (startPrefab == null && straightPrefabs.Count > 0) {
            startPrefab = straightPrefabs[0]; // fallback
        }

        if (straightPrefabs.Count == 0 || turnPrefabs.Count == 0) {
            Debug.LogError("TrackGenerator needs at least one Straight and one Turn prefab!");
            return;
        }
    }

    void Start() {
        // Automatically inject fallback map generation if testing without the MapRotator ML loop!
        if (Object.FindAnyObjectByType<MapRotator>() == null) {
            
            CarDriverAgent[] localCars = Object.FindObjectsByType<CarDriverAgent>(FindObjectsInactive.Include);
            foreach (var c in localCars) {
                if (c != null) c.gameObject.SetActive(false); // Halt physics/ML natively while standalone generation yields
            }

            // Bind anonymous waking routine
            OnTrackGenerated += () => {
                foreach (var c in localCars) {
                    if (c != null) {
                        c.gameObject.SetActive(true);
                        var ctrl = c.GetComponent<CarController>();
                        if (ctrl != null) ctrl.StopCompletely();
                        
                        // Formally invoke the ML-Agent pipeline so Checkpoint indices natively reset back to 0!
                        c.EndEpisode(); 
                    }
                }
            };

            GenerateComplexTrack();
        }
    }

    void ExtractTemplate(GameObject prefab, bool isRightTurn) {
        GameObject tmp = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        tmp.transform.localScale = isRightTurn ? new Vector3(-1, 1, 1) : Vector3.one;

        Transform sPt = tmp.transform.Find("StartPoint");
        Transform ePt = tmp.transform.Find("ExitPoint");

        PieceTemplate tmpl = new PieceTemplate();
        tmpl.prefab = prefab;

        if (sPt != null && ePt != null) {
            float angleToForward = Vector3.SignedAngle(sPt.forward, Vector3.forward, Vector3.up);
            tmp.transform.Rotate(0, angleToForward, 0, Space.World);
            tmp.transform.position += (Vector3.zero - sPt.position);

            tmpl.exitOffset = ePt.position;
            tmpl.exitAngle = Vector3.SignedAngle(Vector3.forward, ePt.forward, Vector3.up);
        } else {
            tmpl.exitOffset = new Vector3(0, 0, prefab.name.Contains("Long") ? 15f : 7.5f);
            tmpl.exitAngle = 0f;
        }

        PieceDef def = new PieceDef { prefab = prefab, isRightTurn = isRightTurn };
        templates[def] = tmpl;
        Destroy(tmp);
    }

    private float maxPieceLength = 0f;


    List<PieceDef> CreateFallbackTrack(int count) {
        int modLength = count / 4;
        List<PieceDef> track = new List<PieceDef>();
        for (int s = 0; s < 4; s++) {
            for (int i = 0; i < modLength - 1; i++) track.Add(new PieceDef { prefab = straightPrefabs[0], isRightTurn = false });
            track.Add(new PieceDef { prefab = turnPrefabs[0], isRightTurn = false });
        }
        return track;
    }

    [Tooltip("The mathematical width of the tracks. Used by the collision algorithm to cast exact footprints.")]
    public float trackWidth = 14f;

    struct Segment {
        public Vector3 a;
        public Vector3 b;
    }

    bool CheckOBBIntersection(Vector3 a3, Vector3 b3, float w1, Vector3 c3, Vector3 d3, float w2) {
        Vector2 a = new Vector2(a3.x, a3.z);
        Vector2 b = new Vector2(b3.x, b3.z);
        Vector2 c = new Vector2(c3.x, c3.z);
        Vector2 d = new Vector2(d3.x, d3.z);

        Vector2 u1 = (b - a);
        if (u1.sqrMagnitude < 0.001f) return false;
        u1.Normalize();
        Vector2 v1 = new Vector2(-u1.y, u1.x); // Perpendicular

        Vector2 u2 = (d - c);
        if (u2.sqrMagnitude < 0.001f) return false;
        u2.Normalize();
        Vector2 v2 = new Vector2(-u2.y, u2.x);

        // Shrink segments linearly by a micro-interval to eliminate false positives strictly at connected joints
        a += u1 * 0.1f;
        b -= u1 * 0.1f;
        c += u2 * 0.1f;
        d -= u2 * 0.1f;
        w1 -= 0.2f;
        w2 -= 0.2f;

        Vector2[] axes = new Vector2[] { u1, v1, u2, v2 };
        foreach (Vector2 ax in axes) {
            float min1 = Mathf.Min(Vector2.Dot(a, ax), Vector2.Dot(b, ax));
            float max1 = Mathf.Max(Vector2.Dot(a, ax), Vector2.Dot(b, ax));
            float exp1 = (w1 / 2f) * Mathf.Abs(Vector2.Dot(v1, ax));
            min1 -= exp1; max1 += exp1;

            float min2 = Mathf.Min(Vector2.Dot(c, ax), Vector2.Dot(d, ax));
            float max2 = Mathf.Max(Vector2.Dot(c, ax), Vector2.Dot(d, ax));
            float exp2 = (w2 / 2f) * Mathf.Abs(Vector2.Dot(v2, ax));
            min2 -= exp2; max2 += exp2;

            if (max1 <= min2 || max2 <= min1) {
                return false; // Separating axis found! No collision geometrically possible.
            }
        }
        return true; 
    }

    class TrackPool {
        public int s;
        public int l;
        public int r;
    }

    class DFSState {
        public Vector3 pos;
        public float rotAngle;
        public int s;
        public int l;
        public int r;
        public List<PieceDef> availableChoices;
        public int choiceIndex;
    }

    private List<PieceDef> GetChoices(int s, int l, int r) {
        List<PieceDef> allChoices = new List<PieceDef>();
        if (s > 0) {
            foreach (var p in straightPrefabs) allChoices.Add(new PieceDef { prefab = p, isRightTurn = false });
        }
        if (l > 0) {
            foreach (var p in turnPrefabs) allChoices.Add(new PieceDef { prefab = p, isRightTurn = false });
        }
        if (r > 0) {
            foreach (var p in turnPrefabs) allChoices.Add(new PieceDef { prefab = p, isRightTurn = true });
        }
        return allChoices.OrderBy(x => Random.value).ToList();
    }

    public void ClearTrack() {
        if (track != null) {
            track.ClearCheckpoints();
        }

        // Strictly delete ONLY geometry explicitly generated by this script to protect ML-Agents and Cameras securely!
        foreach (GameObject piece in activePieces) {
            if (piece != null) {
                piece.SetActive(false); // Force Inspector to disengage UI bindings synchronously!
                Destroy(piece, 0.2f); // Buffer destruction natively by 0.2 seconds logically to safely bridge Editor Hierarchy serialization!
            }
        }
        activePieces.Clear();
    }

    public int maxPreloadedTracks = 3;
    private Queue<GameObject> prebuiltTracks = new Queue<GameObject>();
    private bool isBuildingTrack = false;

    void Update() {
        if (!isBuildingTrack && prebuiltTracks.Count < maxPreloadedTracks && Application.isPlaying) {
            StartCoroutine(TrackGeneratorAsync(false));
        }
    }

    public void GenerateComplexTrack() {
        if (prebuiltTracks.Count > 0) {
            ActivatePrebuiltTrack(prebuiltTracks.Dequeue());
        } else {
            StartCoroutine(TrackGeneratorAsync(true));
        }
    }

    void ActivatePrebuiltTrack(GameObject trackRoot) {
        trackRoot.transform.position = Vector3.zero;
        trackRoot.SetActive(true);
        activePieces.Add(trackRoot);

        if (track != null) {
            track.AddCheckpoints(trackRoot.GetComponentsInChildren<CheckpointSingle>());
        }

        if (OnTrackGenerated != null) OnTrackGenerated.Invoke();
    }

    System.Collections.IEnumerator TrackGeneratorAsync(bool activateImmediately) {
        isBuildingTrack = true;
        
        foreach (var t in templates.Values) {
            maxPieceLength = Mathf.Max(maxPieceLength, t.exitOffset.magnitude);
        }

        int targetTrackLength = trackLength;
        if (randomizeTrackLength) {
            targetTrackLength = Random.Range(minRandomLength, maxRandomLength);
            if (targetTrackLength % 2 != 0) targetTrackLength++; 
        }

        System.Diagnostics.Stopwatch globalSw = new System.Diagnostics.Stopwatch();
        globalSw.Start();
        
        System.Diagnostics.Stopwatch frameSw = new System.Diagnostics.Stopwatch();
        frameSw.Start();

        bool found = false;
        int targetLength = Mathf.Max(4, targetTrackLength);
        List<PieceDef> path = new List<PieceDef>();
        List<Segment> segments = new List<Segment>();

        int nodesEvaluated = 0;

        while (!found && targetLength <= targetTrackLength + 60) {
            List<TrackPool> pools = new List<TrackPool>();
            for (int r = 0; r <= targetLength; r++) {
                int l = r + 4;
                int s = targetLength - l - r;
                if (s >= 0) pools.Add(new TrackPool { s = s, l = l, r = r });
            }
            for (int l = 0; l <= targetLength; l++) {
                int r = l + 4;
                int s = targetLength - l - r;
                if (s >= 0) pools.Add(new TrackPool { s = s, l = l, r = r });
            }

            pools = pools.OrderBy(x => Random.value).ToList();

            foreach (var pool in pools) {
                PieceDef startDef = new PieceDef { prefab = startPrefab != null ? startPrefab : straightPrefabs[0], isRightTurn = false };
                
                int startS = pool.s;
                int startL = pool.l;
                int startR = pool.r;

                if (!turnPrefabs.Contains(startDef.prefab)) startS--;
                else startL--; 

                if (startS < 0 || startL < 0 || startR < 0) continue; 

                path.Clear();
                segments.Clear();
                path.Add(startDef);

                PieceTemplate t = templates[startDef];
                Vector3 nextPos = t.exitOffset;
                float nextRotAngle = t.exitAngle;
                
                segments.Add(new Segment { a = Vector3.zero, b = nextPos * 0.5f });
                segments.Add(new Segment { a = nextPos * 0.5f, b = nextPos });

                Stack<DFSState> stack = new Stack<DFSState>();
                stack.Push(new DFSState { 
                    pos = nextPos, rotAngle = nextRotAngle, 
                    s = startS, l = startL, r = startR, 
                    availableChoices = GetChoices(startS, startL, startR), 
                    choiceIndex = 0 
                });

                int maxDepthReached = 0;
                int localBacktracks = 0;

                while (stack.Count > 0) {
                    nodesEvaluated++;

                    // Seamless dynamic frame-budget yielding!
                    if (frameSw.ElapsedMilliseconds > 8) {
                        yield return null;
                        frameSw.Restart();
                    }

                    if (globalSw.ElapsedMilliseconds > 3000) {
                        Debug.LogWarning("Track evaluation hit global asynchronous thermal timeout. Forcing standard fallback.");
                        break;
                    }
                    if (localBacktracks > 1000) {
                        break; 
                    }

                    DFSState curr = stack.Peek();

                    if (curr.choiceIndex == 0) {
                        if (path.Count > maxDepthReached) maxDepthReached = path.Count;

                        float dist = Vector3.Distance(curr.pos, Vector3.zero);

                        if (curr.s == 0 && curr.l == 0 && curr.r == 0) {
                            if (dist < 1f) {
                                found = true;
                                break;
                            }
                        }

                        int piecesLeft = curr.s + curr.l + curr.r;
                        if (piecesLeft == 0 || dist > piecesLeft * maxPieceLength) {
                            stack.Pop();
                            if (path.Count > stack.Count) {
                                path.RemoveAt(path.Count - 1);
                                segments.RemoveAt(segments.Count - 1);
                                segments.RemoveAt(segments.Count - 1);
                            }
                            continue;
                        }
                    }

                    if (curr.choiceIndex >= curr.availableChoices.Count) {
                        stack.Pop();
                        localBacktracks++; 

                        if (path.Count > stack.Count) {
                            path.RemoveAt(path.Count - 1);
                            segments.RemoveAt(segments.Count - 1);
                            segments.RemoveAt(segments.Count - 1);
                        }
                        continue;
                    }

                    PieceDef choice = curr.availableChoices[curr.choiceIndex++];
                    PieceTemplate ct = templates[choice];
                    Quaternion rot = Quaternion.Euler(0, curr.rotAngle, 0);

                    Vector3 cNextPos = curr.pos + rot * ct.exitOffset;
                    float cNextRotAngle = curr.rotAngle + ct.exitAngle;

                    Segment s1 = new Segment { a = curr.pos, b = (curr.pos + cNextPos) * 0.5f };
                    Segment s2 = new Segment { a = (curr.pos + cNextPos) * 0.5f, b = cNextPos };
                    
                    int nextS = curr.s - (!turnPrefabs.Contains(choice.prefab) ? 1 : 0);
                    int nextL = curr.l - (turnPrefabs.Contains(choice.prefab) && !choice.isRightTurn ? 1 : 0);
                    int nextR = curr.r - (turnPrefabs.Contains(choice.prefab) && choice.isRightTurn ? 1 : 0);

                    bool isLastPiece = (nextS == 0 && nextL == 0 && nextR == 0);
                    int collisionStartIndex = isLastPiece ? 2 : 0;

                    bool collision = false;
                    for (int j = collisionStartIndex; j < segments.Count - 3; j++) {
                        if (CheckOBBIntersection(segments[j].a, segments[j].b, trackWidth, s1.a, s1.b, trackWidth) ||
                            CheckOBBIntersection(segments[j].a, segments[j].b, trackWidth, s2.a, s2.b, trackWidth)) {
                            collision = true;
                            break;
                        }
                    }

                    if (!collision) {
                        path.Add(choice);
                        segments.Add(s1);
                        segments.Add(s2);
                        
                        stack.Push(new DFSState { 
                            pos = cNextPos, rotAngle = cNextRotAngle, 
                            s = nextS, l = nextL, r = nextR,
                            availableChoices = GetChoices(nextS, nextL, nextR), choiceIndex = 0 
                        });
                    }
                }

                if (found) break; // Terminate Pool Search
                
                if (globalSw.ElapsedMilliseconds > 3000) break;
            }

            if (found) break; // Terminate Track Length Iteration
            
            if (globalSw.ElapsedMilliseconds > 3000) break;
            
            targetLength += 2;
        }

        if (!found) {
            Debug.LogWarning("Exhausted all pathing possibilities; generating fallback rectangle track.");
            path = CreateFallbackTrack(targetTrackLength);
            if (startPrefab != null && path.Count > 0) {
                path[0] = new PieceDef { prefab = startPrefab, isRightTurn = false };
            }
        }

        GameObject trackRoot = new GameObject("PrebuiltTrackRoot");
        trackRoot.transform.SetParent(track != null ? track.transform : transform);
        trackRoot.SetActive(false); // keep invisible during buffer
        
        lastExitPoint = trackRoot.transform; // seed rotation point
        bool isFirst = true;
        
        foreach (var def in path) {
            SpawnPieceAsync(def, trackRoot, ref isFirst);
        }
        
        if (activateImmediately) {
            ActivatePrebuiltTrack(trackRoot);
        } else {
            prebuiltTracks.Enqueue(trackRoot);
        }
        
        isBuildingTrack = false;
    }

    void SpawnPieceAsync(PieceDef def, GameObject root, ref bool isFirst) {
        GameObject spawned = Instantiate(def.prefab, Vector3.zero, Quaternion.identity);
        spawned.transform.SetParent(root.transform);
        
        spawned.transform.localScale = def.isRightTurn ? new Vector3(-1, 1, 1) : Vector3.one;

        if (isFirst) {
            spawned.transform.position = transform.position;
            spawned.transform.rotation = transform.rotation;
            isFirst = false;
        } else {
            Transform startPoint = spawned.transform.Find("StartPoint");
            if (startPoint != null) {
                float angle = Vector3.SignedAngle(startPoint.forward, lastExitPoint.forward, Vector3.up);
                spawned.transform.Rotate(0, angle, 0, Space.World);
                spawned.transform.position += (lastExitPoint.position - startPoint.position);
            } else {
                spawned.transform.position = lastExitPoint.position;
                spawned.transform.rotation = lastExitPoint.rotation;
            }
        }

        Transform nextExit = spawned.transform.Find("ExitPoint");
        if (nextExit != null) lastExitPoint = nextExit;
    }
}
