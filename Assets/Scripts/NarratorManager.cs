using System.Collections.Generic;
using UnityEngine;
 
[RequireComponent(typeof(AudioSource))]
public class NarratorManager : MonoBehaviour
{
    public static NarratorManager Instance;
 
    [Header("Audio Source")]
    public AudioSource audioSource;
 
    [Header("Team Scores")]
    [Tooltip("Played when the Red team scores.")]
    public List<AudioClip> redTeamScoresClips;
    [Tooltip("Played when the Blue team scores.")]
    public List<AudioClip> blueTeamScoresClips;
 
    [Header("Near Miss")]
    [Tooltip("Played when the Red team misses.")]
    public List<AudioClip> nearMissRedClips;
    [Tooltip("Played when the Blue team misses.")]
    public List<AudioClip> nearMissBlueClips;
 
    [Header("Controversial Call")]
    [Tooltip("Played when a call goes against the Red team.")]
    public List<AudioClip> controversialCallRedClips;
    [Tooltip("Played when a call goes against the Blue team.")]
    public List<AudioClip> controversialCallBlueClips;
 
    [Header("Game Summary")]
    [Tooltip("Red team is leading when summary fires.")]
    public List<AudioClip> summaryRedLeadingClips;
    [Tooltip("Blue team is leading when summary fires.")]
    public List<AudioClip> summaryBlueLeadingClips;
    [Tooltip("Game is tied.")]
    public List<AudioClip> summaryTiedClips;
 
    [Header("Game End")]
    public List<AudioClip> gameEndRedWinsClips;
    public List<AudioClip> gameEndBlueWinsClips;
    public List<AudioClip> gameEndDrawClips;
 
    [Header("Local Events")]
    [Tooltip("Played when a fight breaks out.")]
    public List<AudioClip> fightClips;
 
    [Header("Settings")]
    [Tooltip("Minimum gap in seconds between two narrator lines.")]
    public float minGapBetweenLines = 1.0f;
    [Tooltip("Extra wait after a clip finishes before the queued clip plays.")]
    public float queueDelay = 0.5f;
 
    // ── Internal state ────────────────────────────────────────────────────────
    private AudioClip _queued          = null;
    private float     _playbackEndTime = -999f; // Negative so the first clip always plays immediately
    private bool      _waitingToPlay   = false;
    private bool      _subscribed      = false;  // Guard against double-subscription
 
    // ── Lifecycle ─────────────────────────────────────────────────────────────
 
    private void Awake()
    {
        Instance = this;
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
 
        audioSource.spatialBlend = 0f;   // Non-diegetic: full volume regardless of camera position
        audioSource.playOnAwake  = false;
    }
 
    /// <summary>
    /// Start() is used for the initial subscription because it is guaranteed to run
    /// AFTER all Awake() calls in the scene — including GameManager.Awake() which sets
    /// GameManager.Instance. Subscribing in OnEnable() is not safe here because OnEnable
    /// fires during scene initialisation before other objects' Awake() calls complete,
    /// causing GameManager.Instance to be null and the subscription to silently fail.
    /// </summary>
    private void Start()
    {
        Subscribe();
    }
 
    /// <summary>
    /// Re-subscribes if this object is toggled off and back on at runtime.
    /// The _subscribed guard prevents double-subscription.
    /// Does nothing during scene initialisation (Start hasn't run yet).
    /// </summary>
    private void OnEnable()
    {
        if (_subscribed) Subscribe();
    }
 
    private void OnDisable()  => Unsubscribe();
    private void OnDestroy()  => Unsubscribe();
 
    private void Subscribe()
    {
        if (_subscribed) return;
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[NarratorManager] Cannot subscribe — GameManager.Instance is null. " +
                             "Ensure GameManager is present in the scene.");
            return;
        }
        GameManager.Instance.OnGlobalEvent += HandleGlobalEvent;
        _subscribed = true;
        Debug.Log("[NarratorManager] Subscribed to GameManager.OnGlobalEvent.");
    }
 
    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (GameManager.Instance != null)
            GameManager.Instance.OnGlobalEvent -= HandleGlobalEvent;
        _subscribed = false;
    }
 
    // ── Update ────────────────────────────────────────────────────────────────
 
    private void Update()
    {
        // Flush the queued clip once the current one finishes and the gap has elapsed
        if (_waitingToPlay && _queued != null && Time.time >= _playbackEndTime + queueDelay)
        {
            PlayNow(_queued);
            _queued        = null;
            _waitingToPlay = false;
        }
    }
 
    // ── Event handler ─────────────────────────────────────────────────────────
 
    private void HandleGlobalEvent(SimEvent evt)
    {
        int score = GameManager.Instance != null ? GameManager.Instance.gameScore : 0;
        AudioClip clip = null;
 
        switch (evt.eventId)
        {
            case "teamScores":
                clip = evt.instigatingTeam == Agent.Teams.Red
                    ? Pick(redTeamScoresClips,            "redTeamScoresClips")
                    : Pick(blueTeamScoresClips,           "blueTeamScoresClips");
                break;
 
            case "nearMiss":
                clip = evt.instigatingTeam == Agent.Teams.Red
                    ? Pick(nearMissRedClips,              "nearMissRedClips")
                    : Pick(nearMissBlueClips,             "nearMissBlueClips");
                break;
 
            case "controversialCall":
                clip = evt.instigatingTeam == Agent.Teams.Red
                    ? Pick(controversialCallRedClips,     "controversialCallRedClips")
                    : Pick(controversialCallBlueClips,    "controversialCallBlueClips");
                break;
 
            case "gameSummary":
                if      (score > 0) clip = Pick(summaryRedLeadingClips,  "summaryRedLeadingClips");
                else if (score < 0) clip = Pick(summaryBlueLeadingClips, "summaryBlueLeadingClips");
                else                clip = Pick(summaryTiedClips,        "summaryTiedClips");
                break;
 
            case "gameEnd":
                if      (score > 0) clip = Pick(gameEndRedWinsClips,     "gameEndRedWinsClips");
                else if (score < 0) clip = Pick(gameEndBlueWinsClips,    "gameEndBlueWinsClips");
                else                clip = Pick(gameEndDrawClips,        "gameEndDrawClips");
                break;
 
            case "fight":
                clip = Pick(fightClips, "fightClips");
                break;
 
            default:
                Debug.Log($"[NarratorManager] No voice line configured for event '{evt.eventId}'.");
                break;
        }
 
        if (clip != null) Request(clip);
    }
 
    // ── Public API ────────────────────────────────────────────────────────────
 
    /// <summary>Request a specific clip from outside (e.g. from agent local events).</summary>
    public void RequestClip(AudioClip clip)
    {
        if (clip != null) Request(clip);
    }
 
    // ── Playback logic ────────────────────────────────────────────────────────
 
    private void Request(AudioClip clip)
    {
        bool gapElapsed = Time.time >= _playbackEndTime + minGapBetweenLines;
 
        if (!audioSource.isPlaying && gapElapsed)
        {
            PlayNow(clip);
        }
        else
        {
            // Replace any stale queued clip — more recent events take priority
            _queued        = clip;
            _waitingToPlay = true;
        }
    }
 
    private void PlayNow(AudioClip clip)
    {
        audioSource.clip = clip;
        audioSource.Play();
        _playbackEndTime = Time.time + clip.length;
        Debug.Log($"[NarratorManager] Playing '{clip.name}' ({clip.length:F1}s).");
    }
 
    // ── Utility ───────────────────────────────────────────────────────────────
 
    private AudioClip Pick(List<AudioClip> list, string listName)
    {
        if (list == null || list.Count == 0)
        {
            Debug.LogWarning($"[NarratorManager] '{listName}' is empty — assign audio clips in the Inspector.");
            return null;
        }
        return list[Random.Range(0, list.Count)];
    }
}