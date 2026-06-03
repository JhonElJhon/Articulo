using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// BeliefSet — agent's current world model
// ─────────────────────────────────────────────────────────────────────────────
/// <summary>
/// Updated every BDI tick via Physics.OverlapSphere perception.
/// Represents what the agent currently "knows" about its environment.
///
/// Setup note: For better performance, assign agents to a dedicated Unity layer
/// named "Agent" and enable it in Physics settings. The perception function will
/// automatically use layer filtering if that layer exists.
/// </summary>
[System.Serializable]
public class BeliefSet
{
    [Range(0f, 1f)]
    public float safetyLevel = 1.0f;    // decreases when fights are nearby

    public int   nearbyAllyCount    = 0;
    public int   nearbyEnemyCount   = 0;
    public Agent nearestEnemy       = null;
    public Agent nearestAlly        = null;
    public Agent nearestDistressedAlly = null;  // ally with mood.P < -0.40

    public bool  isFightNearby      = false;
    public bool  isNearField        = false;    // near pitch boundary

    public int   gameScoreFromMyTeam = 0;       // positive = my team winning

    // Social perception: averaged mood of nearby same-team agents
    public float crowdPleasure  = 0f;
    public float crowdArousal   = 0f;
}

// ─────────────────────────────────────────────────────────────────────────────
// BDIEngine — per-agent decision system
// ─────────────────────────────────────────────────────────────────────────────
/// <summary>
/// Implements the Beliefs-Desires-Intentions model.
///
/// Desires are stable: computed from OCEAN once at init, mood-modulated at runtime.
///
/// Intention selection scores each available action using:
///   score = CosineSimilarity(emotionPAD, actionIdealPAD) * 0.45
///         + MeanDesireFit(action.satisfiesDesires)        * 0.55
///   score *= arousalBoost = 1 + |mood.A| * 0.35
///
/// The winning action is committed to only if its score exceeds the threshold:
///   threshold = 0.40 - conscientiousness * 0.10
/// (conscientious agents deliberate more before acting)
/// </summary>
public class BDIEngine
{
    private readonly Agent _owner;

    public BeliefSet Beliefs { get; } = new BeliefSet();
    public DesireSet Desires { get; } = new DesireSet();

    // Cached layer mask for performance (set to -1 if "Agent" layer doesn't exist)
    private static int _agentLayerMask = -2; // -2 = uninitialised

    public BDIEngine(Agent owner)
    {
        _owner = owner;
    }

    /// <summary>Call once after SetPersonality to initialise desire weights.</summary>
    public void Initialize(OCEAN_Model personality)
    {
        Desires.InitializeFromPersonality(personality);
        EnsureLayerMask();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Perception
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Perceives the environment and updates all beliefs.
    /// Uses GameManager's Spatial Grid for fast lookups.
    /// </summary>
    public void UpdateBeliefs(float perceptionRadius)
    {
        List<Agent> nearby = GameManager.Instance.GetAgentsInRadius(_owner.transform.position, perceptionRadius);

        // Reset
        Beliefs.nearbyAllyCount       = 0;
        Beliefs.nearbyEnemyCount      = 0;
        Beliefs.nearestEnemy          = null;
        Beliefs.nearestAlly           = null;
        Beliefs.nearestDistressedAlly = null;
        Beliefs.isFightNearby         = false;
        Beliefs.crowdPleasure         = 0f;
        Beliefs.crowdArousal          = 0f;

        float closestEnemyDist           = float.MaxValue;
        float closestAllyDist            = float.MaxValue;
        float closestDistressedAllyDist  = float.MaxValue;
        int   fightCount                 = 0;

        foreach (Agent other in nearby)
        {
            if (other == null || other == _owner) continue;

            float dist = Vector3.Distance(_owner.transform.position, other.transform.position);

            if (other.team == _owner.team)
            {
                Beliefs.nearbyAllyCount++;
                Beliefs.crowdPleasure += other.mood.P;
                Beliefs.crowdArousal  += other.mood.A;

                if (dist < closestAllyDist)
                {
                    closestAllyDist   = dist;
                    Beliefs.nearestAlly = other;
                }

                if (other.mood.P < -0.40f && dist < closestDistressedAllyDist)
                {
                    closestDistressedAllyDist     = dist;
                    Beliefs.nearestDistressedAlly = other;
                }
            }
            else
            {
                Beliefs.nearbyEnemyCount++;
                if (dist < closestEnemyDist)
                {
                    closestEnemyDist   = dist;
                    Beliefs.nearestEnemy = other;
                }
            }

            if (other.CurrentAction == AgentActionType.Fight) fightCount++;
        }

        // Safety: proportion of nearby agents NOT fighting
        int total = Mathf.Max(1, Beliefs.nearbyAllyCount + Beliefs.nearbyEnemyCount);
        Beliefs.safetyLevel  = Mathf.Clamp01(1f - (float)fightCount / total);
        Beliefs.isFightNearby = fightCount > 0;

        // Average crowd sentiment
        if (Beliefs.nearbyAllyCount > 0)
        {
            Beliefs.crowdPleasure /= Beliefs.nearbyAllyCount;
            Beliefs.crowdArousal  /= Beliefs.nearbyAllyCount;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Intention Selection
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scores all available actions and returns the best one.
    /// Returns WatchCalmly if no action exceeds the deliberation threshold.
    /// </summary>
    public AgentActionType SelectIntention(EmotionInstance emotion, PADState mood)
    {
        if (emotion.emotion == OCCEmotion.None || emotion.intensity < 0.05f)
            return AgentActionType.WatchCalmly;

        float C  = (float)_owner.conscientiousness;
        float Ag = (float)_owner.agreeableness;

        // Conscientious agents require stronger emotional signal to act
        float threshold = 0.40f - C * 0.10f;

        AgentActionType bestAction = AgentActionType.WatchCalmly;
        float bestScore = float.MinValue;

        foreach (ActionDefinition action in ActionLibrary.All)
        {
            if (!IsPreconditionMet(action, emotion, mood, Ag)) continue;

            float emotionFit = PADValues.CosineSimilarity(emotion.pad, action.idealPAD);

            float desireFit = 0f;
            foreach (DesireType d in action.satisfiesDesires)
                desireFit += Desires.GetEffective(d, mood);
            desireFit /= Mathf.Max(1, action.satisfiesDesires.Length);

            float score = emotionFit * 0.45f + desireFit * 0.55f;

            // Arousal amplifies action likelihood
            score *= 1f + Mathf.Abs(mood.A) * 0.35f;

            if (score > bestScore)
            {
                bestScore  = score;
                bestAction = action.type;
            }
        }

        return bestScore >= threshold ? bestAction : AgentActionType.WatchCalmly;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Precondition checks
    // ─────────────────────────────────────────────────────────────────────────

    private bool IsPreconditionMet(ActionDefinition action, EmotionInstance emotion, PADState mood, float Ag)
    {
        // PAD conditions on the felt emotion
        if (emotion.pad.P < action.minP) return false;
        if (emotion.pad.A < action.minA) return false;
        if (emotion.pad.D < action.minD) return false;

        // World-state conditions
        if (action.requiresNearbyEnemy && Beliefs.nearestEnemy          == null) return false;
        if (action.requiresNearbyAlly  && Beliefs.nearestAlly           == null) return false;
        if (action.requiresLowSafety   && Beliefs.safetyLevel           >= 0.50f) return false;
        if (action.requiresNearField   && !Beliefs.isNearField)                  return false;

        // Personality gates
        if (action.minAgreeableness > 0f && Ag < action.minAgreeableness) return false;
        if (action.type == AgentActionType.PitchInvasion &&
            (float)_owner.conscientiousness > action.maxConscientiousness) return false;

        // Special case: Boo only when feeling negative
        if (action.type == AgentActionType.Boo && emotion.pad.P >= 0f) return false;

        // ThrowObject only when very upset (P < -0.40)
        if (action.type == AgentActionType.ThrowObject && emotion.pad.P > -0.40f) return false;

        // Fight requires some dominance in the felt emotion
        if (action.type == AgentActionType.Fight && emotion.pad.D < 0.15f) return false;

        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Utilities
    // ─────────────────────────────────────────────────────────────────────────

    private static void EnsureLayerMask()
    {
        if (_agentLayerMask != -2) return;
        int layer = LayerMask.NameToLayer("Agent");
        _agentLayerMask = layer >= 0 ? LayerMask.GetMask("Agent") : -1;
        if (layer < 0)
            Debug.LogWarning("[BDI] 'Agent' layer not found. " +
                "OverlapSphere will query ALL colliders — consider adding the layer for performance.");
    }
}
