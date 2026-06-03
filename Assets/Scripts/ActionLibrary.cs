using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// AgentActionType — all possible agent behaviours
// ─────────────────────────────────────────────────────────────────────────────
public enum AgentActionType
{
    WatchCalmly,    // Default: wander to POIs
    Celebrate,      // Fast excited movement after positive event
    Chant,          // Stand still facing the field (group energy)
    Boo,            // Stand still facing the field (negative energy)
    Insult,         // Move toward nearest enemy and provoke
    Fight,          // Chase nearest enemy and physically confront
    Run,            // Move away from threat at high speed
    ComfortAlly,    // Approach a distressed ally
    CalmSituation,  // Move toward a fight to de-escalate
    FormGroup,      // Move toward allied cluster
    PitchInvasion,  // Run toward field (impulsive celebration)
    ThrowObject     // Stand still (throw gesture toward field)
}

// ─────────────────────────────────────────────────────────────────────────────
// ActionDefinition
// ─────────────────────────────────────────────────────────────────────────────
/// <summary>
/// Describes a single action: which PAD state it expresses,
/// which desires it satisfies, and under what conditions it is available.
///
/// The BDI engine scores actions using:
///   score = CosineSimilarity(agent.currentEmotionPAD, action.idealPAD) * 0.45
///         + DesireFit(action.satisfiesDesires, agent.desires)          * 0.55
///   score *= 1 + |mood.A| * 0.35  (arousal boost)
/// </summary>
public class ActionDefinition
{
    public AgentActionType type;

    /// <summary>The PAD emotional state this action naturally expresses.</summary>
    public PADValues idealPAD;

    /// <summary>The desires this action satisfies.</summary>
    public DesireType[] satisfiesDesires;

    // ── PAD preconditions (checked against the agent's current emotion PAD) ──
    public float minP = -1f;
    public float minA = -1f;
    public float minD = -1f;

    // ── Belief-based preconditions ────────────────────────────────────────────
    public bool requiresNearbyEnemy = false;
    public bool requiresNearbyAlly  = false;
    public bool requiresLowSafety   = false;
    public bool requiresNearField   = false;

    // ── Personality gates ─────────────────────────────────────────────────────
    /// <summary>ComfortAlly only activates on agents with enough Agreeableness.</summary>
    public float minAgreeableness = 0f;

    /// <summary>PitchInvasion only activates on impulsive (low-C) agents.</summary>
    public float maxConscientiousness = 1f;
}

// ─────────────────────────────────────────────────────────────────────────────
// ActionLibrary — static registry of all action definitions
// ─────────────────────────────────────────────────────────────────────────────
public static class ActionLibrary
{
    private static readonly List<ActionDefinition> _all = new List<ActionDefinition>
    {
        new ActionDefinition
        {
            type             = AgentActionType.WatchCalmly,
            idealPAD         = new PADValues( 0.00f,  0.00f,  0.00f),
            satisfiesDesires = new[] { DesireType.Safety },
        },

        new ActionDefinition
        {
            type             = AgentActionType.Celebrate,
            idealPAD         = new PADValues( 0.80f,  0.60f,  0.30f),
            satisfiesDesires = new[] { DesireType.Win, DesireType.Celebrate, DesireType.Social },
            minP             = 0.15f,
        },

        new ActionDefinition
        {
            type             = AgentActionType.Chant,
            idealPAD         = new PADValues( 0.50f,  0.50f,  0.20f),
            satisfiesDesires = new[] { DesireType.Win, DesireType.Social },
            minP             = 0.05f,
            requiresNearbyAlly = true,
        },

        new ActionDefinition
        {
            type             = AgentActionType.Boo,
            idealPAD         = new PADValues(-0.40f,  0.40f,  0.00f),
            satisfiesDesires = new[] { DesireType.Vent, DesireType.Win },
        },

        new ActionDefinition
        {
            type             = AgentActionType.Insult,
            idealPAD         = new PADValues(-0.50f,  0.60f,  0.40f),
            satisfiesDesires = new[] { DesireType.Vent, DesireType.Dominate },
            requiresNearbyEnemy = true,
            minD             = -0.10f,
        },

        new ActionDefinition
        {
            type             = AgentActionType.Fight,
            idealPAD         = new PADValues(-0.60f,  0.70f,  0.60f),
            satisfiesDesires = new[] { DesireType.Vent, DesireType.Dominate },
            requiresNearbyEnemy = true,
            minD             = 0.20f,
        },

        new ActionDefinition
        {
            type             = AgentActionType.Run,
            idealPAD         = new PADValues(-0.50f,  0.30f, -0.40f),
            satisfiesDesires = new[] { DesireType.Safety },
            requiresLowSafety = true,
        },

        new ActionDefinition
        {
            type             = AgentActionType.ComfortAlly,
            idealPAD         = new PADValues( 0.00f, -0.20f,  0.20f),
            satisfiesDesires = new[] { DesireType.Social, DesireType.Order },
            requiresNearbyAlly = true,
            minAgreeableness  = 0.40f,
        },

        new ActionDefinition
        {
            type             = AgentActionType.CalmSituation,
            idealPAD         = new PADValues( 0.10f, -0.30f,  0.30f),
            satisfiesDesires = new[] { DesireType.Order },
            requiresNearbyEnemy = true,
        },

        new ActionDefinition
        {
            type             = AgentActionType.FormGroup,
            idealPAD         = new PADValues( 0.40f,  0.40f,  0.10f),
            satisfiesDesires = new[] { DesireType.Social, DesireType.Win },
            requiresNearbyAlly = true,
            minP             = 0.00f,
        },

        new ActionDefinition
        {
            type             = AgentActionType.PitchInvasion,
            idealPAD         = new PADValues( 0.90f,  0.90f,  0.70f),
            satisfiesDesires = new[] { DesireType.Win, DesireType.Celebrate, DesireType.Dominate },
            minP             = 0.65f,
            requiresNearField = true,
            maxConscientiousness = 0.35f,
        },

        new ActionDefinition
        {
            type             = AgentActionType.ThrowObject,
            idealPAD         = new PADValues(-0.70f,  0.80f,  0.50f),
            satisfiesDesires = new[] { DesireType.Vent, DesireType.Dominate },
        },
    };

    public static IReadOnlyList<ActionDefinition> All => _all;

    public static ActionDefinition Get(AgentActionType type) =>
        _all.Find(a => a.type == type);
}
