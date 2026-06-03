using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Central simulation manager.
///
/// Keyboard controls (after pressing Enter to spawn):
///   E       — Red team scores
///   Q       — Blue team scores
///   F       — Game summary (agents reflect on current score)
///   N       — Near miss (Red team)
///   M       — Near miss (Blue team)
///   V       — Controversial referee call against Red
///   B       — Controversial referee call against Blue
///   Space   — End game
///
/// Events fire via the OnGlobalEvent C# event, which all Agents subscribe to.
/// Local events use BroadcastLocalEvent with a queue and OverlapSphereNonAlloc.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    // ── Scene Setup ───────────────────────────────────────────────────────────
    [Header("NPC Settings")]
    public int        maxNPC    = 480;
    public GameObject npcPrefab;
    public int maxIterations = 1;
    private int iterationCount = 0;
    public float eventCooldown = 5f;

    [HideInInspector]
    public List<Agent> allAgents = new List<Agent>(); // Track all agents for the Spatial Grid
    [HideInInspector]
    public Transform[] exitPoints; // Cached exits for fleeing agents

    [Header("Personality Distribution")]
    public List<PersonalityCount> personalityCounts = new List<PersonalityCount>();

    [Header("Game State")]
    public int   gameScore      = 0;  // Positive = Red leads, Negative = Blue leads
    public float timeRemaining  = 90f; // Match time in seconds
    public float crono = 0f;

    [System.Serializable]
    public struct PersonalityCount
    {
        public OCEAN_Model.Frames personality;
        public int count;
    }

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Subscribe to receive all global simulation events.</summary>
    public event Action<SimEvent> OnGlobalEvent;
    public event Action ExecuteAction;

    private bool _spawned = false;
    private bool _gameOver = false;

    // Queue for local events to prevent StackOverflowException and optimize
    private struct PendingLocalEvent {
        public SimEvent evt;
        public Vector3 origin;
        public float radius;
    }
    private Queue<PendingLocalEvent> _localEventQueue = new Queue<PendingLocalEvent>();
    private int _maxEventsPerFrame = 15; // Throttle to prevent frame spikes

    // Explicit loop tracking
    private bool _isProcessingSimulation = false;
    private float _waitTimer = 0f;

    // Spatial Grid for O(1) proximity lookups (replacing Physics.OverlapSphere)
    private float _gridCellSize = 5f;
    private Dictionary<Vector2Int, List<Agent>> _spatialGrid = new Dictionary<Vector2Int, List<Agent>>();

    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        Instance = this;
        GameObject[] exits = GameObject.FindGameObjectsWithTag("Exit");
        exitPoints = new Transform[exits.Length];
        for (int i = 0; i < exits.Length; i++) exitPoints[i] = exits[i].transform;
    }

    private void Update()
    {
        if (_spawned) RebuildSpatialGrid();
        
        if (_isProcessingSimulation)
        {
            if (_waitTimer > 0f) 
            {
                _waitTimer -= Time.deltaTime;
            }
            else
            {
                Debug.Log("queue: " + _localEventQueue.Count);
                ProcessLocalEvents();
            }
        }

        if (Input.GetKeyDown(KeyCode.Return) && !_spawned && !_gameOver)
        {
            SpawnAllNPCs();
            _spawned = true;
        }
        if(_spawned)crono += Time.deltaTime;
        // Cosmetic countdown
        if (_spawned && !_gameOver && timeRemaining > 0f) timeRemaining -= Time.deltaTime;

        //if (eventCooldown > 0f) eventCooldown -= Time.deltaTime;

        if (!_spawned || _gameOver) return;

        if (Input.GetKeyDown(KeyCode.E))     TriggerTeamScores(Agent.Teams.Red);
        if (Input.GetKeyDown(KeyCode.Q))     TriggerTeamScores(Agent.Teams.Blue);
        if (Input.GetKeyDown(KeyCode.F))     TriggerGameSummary();
        if (Input.GetKeyDown(KeyCode.N))     TriggerNearMiss(Agent.Teams.Red);
        if (Input.GetKeyDown(KeyCode.M))     TriggerNearMiss(Agent.Teams.Blue);
        if (Input.GetKeyDown(KeyCode.V))     TriggerControversialCall(Agent.Teams.Red);
        if (Input.GetKeyDown(KeyCode.B))     TriggerControversialCall(Agent.Teams.Blue);
        if (Input.GetKeyDown(KeyCode.Space)) TriggerGameEnd();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Global event triggers (called from input or external game systems)
    // ─────────────────────────────────────────────────────────────────────────

    public void TriggerTeamScores(Agent.Teams scoringTeam)
    {
        if (scoringTeam == Agent.Teams.Red) gameScore++;
        else gameScore--;

        BroadcastGlobalEvent(SimEvent.TeamScores(scoringTeam));
        Debug.Log($"[Game] {scoringTeam} scores! Score: {gameScore}");
    }

    public void TriggerNearMiss(Agent.Teams attemptingTeam)
    {
        BroadcastGlobalEvent(SimEvent.NearMiss(attemptingTeam));
        Debug.Log($"[Game] Near miss by {attemptingTeam}!");
    }

    public void TriggerControversialCall(Agent.Teams disadvantagedTeam)
    {
        BroadcastGlobalEvent(SimEvent.ControversialCall(disadvantagedTeam));
        Debug.Log($"[Game] Controversial call against {disadvantagedTeam}!");
    }

    public void TriggerGameSummary()
    {
        Agent.Teams favored = gameScore >= 0 ? Agent.Teams.Red : Agent.Teams.Blue;
        BroadcastGlobalEvent(SimEvent.GameSummary(favored, Mathf.Abs(gameScore)));
        Debug.Log($"[Game] Summary broadcast. Current score: {gameScore}");
    }

    public void TriggerGameEnd()
    {
        Agent.Teams winner = gameScore >= 0 ? Agent.Teams.Red : Agent.Teams.Blue;
        BroadcastGlobalEvent(SimEvent.GameEnd(winner, Mathf.Abs(gameScore)));
        Debug.Log($"[Game] GAME OVER. Winner: {winner}. Score: {gameScore}");
        _gameOver = true; // Prevent further events
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Event broadcasting
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fires a global event to all subscribed agents simultaneously.</summary>
    public void BroadcastGlobalEvent(SimEvent evt)
    {
        // 1. All agents LISTEN (update emotion/mood)
        OnGlobalEvent?.Invoke(evt);
        
        // 2. Start Phase 2: React
        _localEventQueue.Clear();
        iterationCount = 0;
        ExecuteAction?.Invoke(); // Command agents to submit their reaction
        
        _waitTimer = eventCooldown; // Delay before processing local events
        _isProcessingSimulation = true;
    }

    /// <summary>
    /// Broadcasts a local event to all agents within <paramref name="radius"/> of <paramref name="origin"/>.
    ///
    /// Performance note: this uses a queue and a Spatial Grid
    /// to avoid StackOverflowException and minimize allocations when handling large crowds.
    /// </summary>
    public void BroadcastLocalEvent(SimEvent evt, Vector3 origin, float radius)
    {
        _localEventQueue.Enqueue(new PendingLocalEvent { evt = evt, origin = origin, radius = radius });
    }

    private void ProcessLocalEvents()
    {
        int count = _localEventQueue.Count;
        for (int i = 0; i < count; i++)
        {
            var pEvent = _localEventQueue.Dequeue();
            List<Agent> nearbyAgents = GetAgentsInRadius(pEvent.origin, pEvent.radius);
            
            foreach (Agent agent in nearbyAgents)
            {
                if (agent != pEvent.evt.instigator)
                {
                    agent.ReceiveLocalEvent(pEvent.evt);
                }
            }
        }
        
        iterationCount++;
        Debug.Log("Iteration " + iterationCount + " complete.");

        if(iterationCount < maxIterations)
        {
            // Command agents to submit reactions based on the current local events
            ExecuteAction?.Invoke();
            _waitTimer = eventCooldown;
        }
        else
        {
            // Done with all iterations
            _isProcessingSimulation = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Spatial Grid (Optimization)
    // ─────────────────────────────────────────────────────────────────────────

    private Vector2Int GetGridCell(Vector3 pos)
    {
        return new Vector2Int(Mathf.FloorToInt(pos.x / _gridCellSize), Mathf.FloorToInt(pos.z / _gridCellSize));
    }

    private void RebuildSpatialGrid()
    {
        // Rebuilding the grid every frame is extremely fast (O(N) dictionary groupings)
        // and completely eliminates Physics.OverlapSphere CPU bounds overhead.
        _spatialGrid.Clear();
        foreach (Agent agent in allAgents)
        {
            if (agent == null) continue;
            Vector2Int cell = GetGridCell(agent.transform.position);
            if (!_spatialGrid.TryGetValue(cell, out List<Agent> list))
            {
                list = new List<Agent>();
                _spatialGrid[cell] = list;
            }
            list.Add(agent);
        }
    }

    /// <summary>Fast proximity lookup using the Spatial Grid without Unity Physics.</summary>
    public List<Agent> GetAgentsInRadius(Vector3 origin, float radius)
    {
        List<Agent> results = new List<Agent>();
        float sqrRadius = radius * radius;
        int cellRadius = Mathf.CeilToInt(radius / _gridCellSize);
        Vector2Int centerCell = GetGridCell(origin);

        for (int x = -cellRadius; x <= cellRadius; x++)
        {
            for (int y = -cellRadius; y <= cellRadius; y++)
            {
                Vector2Int checkCell = new Vector2Int(centerCell.x + x, centerCell.y + y);
                if (_spatialGrid.TryGetValue(checkCell, out List<Agent> occupants))
                {
                    foreach (Agent agent in occupants)
                    {
                        if ((agent.transform.position - origin).sqrMagnitude <= sqrRadius)
                        {
                            results.Add(agent);
                        }
                    }
                }
            }
        }
        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Spawning
    // ─────────────────────────────────────────────────────────────────────────

    private void SpawnAllNPCs()
    {
        List<OCEAN_Model.Frames> assignments = BuildAssignmentList();

        GameObject[] pois = GameObject.FindGameObjectsWithTag("POI");
        if (pois.Length == 0)
        {
            Debug.LogError("[GameManager] No objects tagged 'POI' found. Cannot spawn.");
            return;
        }
        pois = ShuffleArray(pois);

        // Determine Agent layer (optional — for BDI OverlapSphere performance)
        int agentLayer = LayerMask.NameToLayer("Agent");

        int spawnCount = Mathf.Min(maxNPC, pois.Length);
        for (int i = 0; i < spawnCount; i++)
        {
            GameObject npcGO = Instantiate(npcPrefab);

            // Optionally place on Agent layer if it exists
            if (agentLayer >= 0) npcGO.layer = agentLayer;

            NavMeshAgent nav = npcGO.GetComponent<NavMeshAgent>();
            if (nav != null) nav.Warp(pois[i].transform.position);
            else npcGO.transform.position = pois[i].transform.position;

            Agent agent = npcGO.GetComponent<Agent>();
            if (agent != null)
            {
                agent.SetTeam(Agent.Teams.Random);
                agent.SetPersonality(assignments[i]);
                allAgents.Add(agent); // Add to master list for Spatial Grid
            }
        }

        Debug.Log($"[GameManager] Spawned {spawnCount} NPCs.");
    }

    private List<OCEAN_Model.Frames> BuildAssignmentList()
    {
        var list  = new List<OCEAN_Model.Frames>();
        int total = 0;

        foreach (var pc in personalityCounts)
        {
            if (pc.personality == OCEAN_Model.Frames.Random) continue;
            for (int i = 0; i < pc.count; i++) list.Add(pc.personality);
            total += pc.count;
        }

        // Fill remainder with Random
        for (int i = 0; i < maxNPC - total; i++)
            list.Add(OCEAN_Model.Frames.Random);

        return ShuffleList(list);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Utilities
    // ─────────────────────────────────────────────────────────────────────────

    private List<T> ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = UnityEngine.Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    private T[] ShuffleArray<T>(T[] arr)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            int j = UnityEngine.Random.Range(i, arr.Length);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return arr;
    }

    private void OnValidate()
    {
        int total = 0;
        foreach (var pc in personalityCounts)
        {
            if (pc.personality == OCEAN_Model.Frames.Random)
                Debug.LogWarning("[GameManager] 'Random' in personalityCounts is redundant.");
            total += pc.count;
        }
        if (total > maxNPC)
            Debug.LogWarning($"[GameManager] Sum of counts ({total}) exceeds maxNPC ({maxNPC}).");
    }
}
