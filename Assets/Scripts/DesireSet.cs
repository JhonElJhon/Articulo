using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// DesireType — the goals an agent cares about
// ─────────────────────────────────────────────────────────────────────────────
public enum DesireType
{
    Win,        // Wants their team to win
    Safety,     // Wants to avoid physical harm
    Social,     // Wants to belong to the crowd
    Vent,       // Wants to express negative emotions outward
    Celebrate,  // Wants to express positive emotions
    Order,      // Wants the situation to be calm and controlled
    Dominate    // Wants to assert power or intimidate others
}

// ─────────────────────────────────────────────────────────────────────────────
// DesireSet
// ─────────────────────────────────────────────────────────────────────────────
/// <summary>
/// Holds the weighted desire profile for an agent.
///
/// Base weights are computed ONCE from OCEAN personality at initialization.
/// They are stable across the simulation — personality doesn't change.
///
/// Runtime weights apply a mood modifier: arousal amplifies everything,
/// displeasure specifically boosts Vent and Safety.
///
/// Design rationale:
///   Win       = competitive agents (low A, high E) care most about score.
///   Safety    = neurotic introverts prioritize avoiding danger.
///   Social    = extraverted, agreeable agents want to be part of the crowd.
///   Vent      = neurotic, disagreeable agents need to release frustration.
///   Celebrate = open, extraverted agents express joy outwardly.
///   Order     = conscientious, agreeable agents maintain decorum.
///   Dominate  = extraverted, disagreeable agents assert themselves.
/// </summary>
[System.Serializable]
public class DesireSet
{
    [Range(0f, 1f)] public float Win;
    [Range(0f, 1f)] public float Safety;
    [Range(0f, 1f)] public float Social;
    [Range(0f, 1f)] public float Vent;
    [Range(0f, 1f)] public float Celebrate;
    [Range(0f, 1f)] public float Order;
    [Range(0f, 1f)] public float Dominate;

    /// <summary>
    /// Computes base desire weights from OCEAN personality.
    /// Called once when an agent is initialized.
    /// </summary>
    public void InitializeFromPersonality(OCEAN_Model p)
    {
        float O  = (float)p.Openness;
        float C  = (float)p.Conscientiousness;
        float E  = (float)p.Extraversion;
        float Ag = (float)p.Agreeableness;
        float N  = (float)p.Neuroticism;

        Win       = Mathf.Clamp01(0.40f + E * 0.20f + (1f - Ag) * 0.20f);
        Safety    = Mathf.Clamp01(0.30f + N * 0.50f - E * 0.15f);
        Social    = Mathf.Clamp01(0.20f + E * 0.40f + Ag * 0.30f);
        Vent      = Mathf.Clamp01(0.10f + N * 0.35f + (1f - Ag) * 0.40f);
        Celebrate = Mathf.Clamp01(0.25f + E * 0.30f + O * 0.20f);
        Order     = Mathf.Clamp01(0.10f + C * 0.50f + Ag * 0.30f);
        Dominate  = Mathf.Clamp01(0.10f + E * 0.25f + (1f - Ag) * 0.35f);
    }

    /// <summary>
    /// Returns the effective desire weight for a type, adjusted by current mood.
    /// Arousal amplifies all desires (you're activated).
    /// Displeasure specifically strengthens the urge to vent and seek safety.
    /// </summary>
    public float GetEffective(DesireType desire, PADState mood)
    {
        float baseWeight  = GetBase(desire);
        float arousalAmp  = 1f + Mathf.Abs(mood.A) * 0.30f;

        if (mood.P < -0.20f)
        {
            if (desire == DesireType.Vent)
                baseWeight *= (1f + Mathf.Abs(mood.P) * 0.40f);
            if (desire == DesireType.Safety)
                baseWeight *= (1f + Mathf.Abs(mood.P) * 0.25f);
        }

        return Mathf.Clamp01(baseWeight * arousalAmp);
    }

    private float GetBase(DesireType desire) => desire switch
    {
        DesireType.Win       => Win,
        DesireType.Safety    => Safety,
        DesireType.Social    => Social,
        DesireType.Vent      => Vent,
        DesireType.Celebrate => Celebrate,
        DesireType.Order     => Order,
        DesireType.Dominate  => Dominate,
        _                    => 0f
    };
}
