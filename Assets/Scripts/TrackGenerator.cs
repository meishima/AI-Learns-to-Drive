using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class TrackGenerator : MonoBehaviour
{
    public GameObject[] trackPrefabs;
    public int trackLength = 20;
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
    
    private Dictionary<PieceDef, PieceTemplate> templates = new Dictionary<PieceDef, PieceTemplate>();

    void Start() {
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

        GenerateComplexTrack();
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

    void GenerateComplexTrack() {
        foreach (var t in templates.Values) {
            maxPieceLength = Mathf.Max(maxPieceLength, t.exitOffset.magnitude);
        }
        Time.timeScale = 0f;
        StartCoroutine(TrackGeneratorCoroutine());
    }

    System.Collections.IEnumerator TrackGeneratorCoroutine() {
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        bool found = false;
        int targetLength = Mathf.Max(4, trackLength);
        List<PieceDef> path = new List<PieceDef>();
        List<Segment> segments = new List<Segment>();

        int framesYielded = 0;
        int nodesEvaluated = 0;

        while (!found && targetLength <= trackLength + 60) {
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
                else startL--; // Assume originally authored prefabs are Left Turns

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

                int frameThrottle = 0;
                int maxDepthReached = 0;

                while (stack.Count > 0) {
                    nodesEvaluated++;

                    if (sw.ElapsedMilliseconds > 15 || frameThrottle++ > 2000) {
                        if (framesYielded % 30 == 0) {
                            Debug.Log($"[Track Generator] L-R=4 Pool ({pool.s}/{pool.l}/{pool.r}) | Target Length {targetLength} | Nodes Explored: {nodesEvaluated} | Max Path Discovered: {maxDepthReached} pieces");
                        }
                        framesYielded++;
                        yield return null;
                        sw.Restart();
                        frameThrottle = 0;
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
            }

            if (found) break; // Terminate Track Length Iteration
            
            targetLength += 2;
            Debug.Log("Increasing target track length to " + targetLength + " to find viable layout...");
        }

        if (!found) {
            Debug.LogWarning("Exhausted all pathing possibilities; generating fallback rectangle track.");
            path = CreateFallbackTrack(trackLength);
            if (startPrefab != null && path.Count > 0) {
                path[0] = new PieceDef { prefab = startPrefab, isRightTurn = false };
            }
        }

        lastExitPoint = transform;
        bool isFirst = true;
        foreach (var def in path) {
            SpawnPiece(def, ref isFirst);
        }
        Time.timeScale = 1f;
    }

    void SpawnPiece(PieceDef def, ref bool isFirst) {
        GameObject spawned = Instantiate(def.prefab, Vector3.zero, Quaternion.identity);
        spawned.transform.SetParent(track != null ? track.transform : transform);
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

        if (track != null) {
            track.AddCheckpoints(spawned.GetComponentsInChildren<CheckpointSingle>());
        }
    }
}
