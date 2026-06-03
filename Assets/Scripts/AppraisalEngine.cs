using UnityEngine;

/// <summary>
/// Stateless OCC appraisal engine.
///
/// The key insight: events carry raw APPRAISAL SIGNALS, not pre-assigned emotions.
/// This function runs on each individual agent, so the same event can produce
/// completely different emotions depending on personality, mood, and allegiance.
///
/// Pipeline:
///   1. Flip desirability sign based on team allegiance.
///   2. Amplify negative desirability with Neuroticism.
///   3. Compute perceived dominance from E, N, mood.D, physical threat.
///      → This is what separates Fear / Anger / Distress for the same event.
///   4. Select OCC emotion using agency, perceived dominance, and personality gates.
///   5. Compute intensity from desirability × surprise × social × mood momentum.
///   6. Apply Mehrabian personality modifier to the PAD values.
/// </summary>
public static class AppraisalEngine
{
    // Agents below this absolute desirability won't react at all.
    private const float EmotionThreshold = 0.08f;

    // How strongly personality modifies the emotion's PAD values (Mehrabian weight).
    private const float PersonalityWeight = 0.5f;

    // ─────────────────────────────────────────────────────────────────────────
    public static EmotionInstance Appraise(SimEvent evt, Agent agent)
    {
        // ── Step 1: Effective desirability from this agent's perspective ──────
        float sign        = (agent.team == evt.instigatingTeam) ? 1f : -1f;
        float desirability = evt.desirability * sign;

        // Neuroticism amplifies the pain of negative events only.
        float N = (float)agent.neuroticism;
        if (desirability < 0f)
            desirability *= (1f + N * 0.60f);

        desirability = Mathf.Clamp(desirability, -1f, 1f);

        if (Mathf.Abs(desirability) < EmotionThreshold)
            return EmotionInstance.None;

        // ── Step 2: Perceived Dominance ───────────────────────────────────────
        // This single value determines whether a negative event produces
        // Fear (D < 0 → flight), Anger (D > 0 → fight), or Distress (D ≈ 0 → freeze).
        // It is computed from personality + current mood — NOT from the event.
        // Two agents receiving the same insult will compute different perceivedD
        // based on their E, N, and accumulated mood history.
        float E = (float)agent.extraversion;
        float perceivedD = E * 0.50f
                         - N * 0.35f
                         + agent.mood.D * 0.30f
                         - evt.physicalThreat * (1f - E * 0.40f);
        perceivedD = Mathf.Clamp(perceivedD, -1f, 1f);

        // ── Step 3: Select OCC emotion ────────────────────────────────────────
        OCCEmotion emotion = SelectEmotion(evt, agent, desirability, perceivedD, N, E);

        // ── Step 4: Compute intensity ─────────────────────────────────────────
        // Base intensity from desirability magnitude, boosted by:
        //   - Surprise: unexpected events hit harder.
        //   - Social:   visible events hit extraverted agents harder.
        //   - Momentum: existing negative mood makes bad things worse.
        float baseIntensity = Mathf.Abs(desirability);
        float surprise      = evt.suddenness   * 0.30f;
        float social        = evt.socialWeight * E * 0.20f;
        float momentum      = Mathf.Abs(agent.mood.P) * 0.20f;

        float intensity = Mathf.Clamp(
            baseIntensity * (1f + surprise + social + momentum),
            0f, 1f);

        // ── Step 5: Scale and modify PAD values ───────────────────────────────
        PADValues basePAD = OCCEmotionData.GetPAD(emotion);
        PADValues scaled  = new PADValues(
            basePAD.P * intensity,
            basePAD.A * intensity,
            basePAD.D * intensity);

        PADValues finalPAD = ApplyPersonalityModifier(scaled, agent, PersonalityWeight);

        return new EmotionInstance
        {
            emotion   = emotion,
            intensity = intensity,
            pad       = finalPAD
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Emotion selection logic
    // ─────────────────────────────────────────────────────────────────────────
    private static OCCEmotion SelectEmotion(
        SimEvent evt, Agent agent,
        float desirability, float perceivedD,
        float N, float E)
    {
        float Ag = (float)agent.agreeableness;
        float C  = (float)agent.conscientiousness;

        // ── Positive branch ───────────────────────────────────────────────────
        if (desirability > 0.1f)
        {
            OCCEmotion emotion = evt.agency switch
            {
                EventAgency.Self => OCCEmotion.Pride,   // "I did something good"
                EventAgency.Ally => OCCEmotion.HappyFor, // "My side did something good"
                _                => OCCEmotion.Joy       // "Good thing happened"
            };

            // Conscientious agents feel Satisfaction for expected rewards
            if (C > 0.60f && evt.suddenness < 0.30f)
                emotion = OCCEmotion.Satisfaction;

            // Very strong, public positive moment → full Gratification
            if (desirability > 0.70f && evt.socialWeight > 0.80f)
                emotion = OCCEmotion.Gratification;

            // Another agent did something praiseworthy → Admiration
            if (evt.praiseworthiness > 0.60f && evt.agency == EventAgency.Ally)
                emotion = OCCEmotion.Admiration;

            return emotion;
        }

        // ── Negative branch ───────────────────────────────────────────────────

        float threat = evt.physicalThreat;

        // Physical danger: perceived dominance decides fight-or-flight.
        if (threat > 0.50f)
            return perceivedD < 0f ? OCCEmotion.Fear : OCCEmotion.Anger;

        // An agent caused this.
        if (evt.agency == EventAgency.Enemy || evt.agency == EventAgency.Ally)
        {
            OCCEmotion emotion;

            if      (perceivedD >  0.20f) emotion = OCCEmotion.Anger;    // fight
            else if (perceivedD < -0.20f) emotion = OCCEmotion.Fear;     // flight
            else                          emotion = OCCEmotion.Distress;  // freeze

            // Agreeable agents reproach rather than rage — less activation, more judgment.
            if (Ag > 0.65f && Mathf.Abs(desirability) < 0.65f)
                emotion = OCCEmotion.Reproach;

            // Very intense hatred → upgrade Anger to Hate.
            if (emotion == OCCEmotion.Anger && desirability < -0.70f)
                emotion = OCCEmotion.Hate;

            return emotion;
        }

        // Neutral agency (game, circumstance, misfortune).
        if (N > 0.60f)   return OCCEmotion.FearsConfirmed; // "I knew this would happen"
        if (perceivedD > 0f) return OCCEmotion.Disappointment;
        return OCCEmotion.Distress;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mehrabian (1996) personality modifier
    // Inverse equations (Big-Five → PAD):
    //   δP = 0.59·Ag + 0.25·S + 0.19·E
    //   δA = −0.65·S + 0.42·Ag
    //   δD =  0.77·E − 0.27·Ag + 0.21·O
    // ─────────────────────────────────────────────────────────────────────────
    private static PADValues ApplyPersonalityModifier(PADValues pad, Agent agent, float weight)
    {
        float O  = (float)agent.openness;
        float E  = (float)agent.extraversion;
        float Ag = (float)agent.agreeableness;
        float S  = (float)agent.stability;

        float dP = 0.59f * Ag + 0.25f * S + 0.19f * E;
        float dA = -0.65f * S + 0.42f * Ag;
        float dD =  0.77f * E - 0.27f * Ag + 0.21f * O;

        return new PADValues(
            Mathf.Clamp(pad.P + weight * dP, -1f, 1f),
            Mathf.Clamp(pad.A + weight * dA, -1f, 1f),
            Mathf.Clamp(pad.D + weight * dD, -1f, 1f));
    }
}
