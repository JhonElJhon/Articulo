using UnityEngine;

/// <summary>
/// Persistent mood state in PAD space. Accumulates emotion influences over time
/// and naturally decays toward neutral. Feeds back into the OCC appraisal engine.
///
/// Key behaviours:
///   - Learning rate scales with Neuroticism: neurotic agents shift mood faster.
///   - Decay rate scales with Stability: stable agents return to baseline faster.
///   - Mood feeds back into perceived dominance, amplifying or dampening future emotions.
/// </summary>
[System.Serializable]
public class PADState
{
    public float P; // Pleasure   [-1, 1]
    public float A; // Arousal    [-1, 1]
    public float D; // Dominance  [-1, 1]

    public PADValues Values => new PADValues(P, A, D);

    /// <summary>
    /// Blends a new emotion's PAD delta into the current mood.
    /// Higher neuroticism → faster mood shift (more reactive).
    /// </summary>
    public void UpdateFromEmotion(EmotionInstance emotion, float neuroticism)
    {
        // N ∈ [0.10, 0.25] learning rate range
        float lr = 0.10f + neuroticism * 0.15f;

        // Weighted blend: mood moves toward the emotion's PAD
        P = P + lr * (emotion.pad.P - P);
        A = A + lr * (emotion.pad.A - A);
        D = D + lr * (emotion.pad.D - D);

        Clamp();
    }

    /// <summary>
    /// Natural decay toward neutral (P=0, A=0, D=0).
    /// Called every Update tick. Higher stability → faster recovery.
    /// </summary>
    /// <param name="stability">Agent's stability value (1 - Neuroticism).</param>
    /// <param name="deltaTime">Time.deltaTime from the calling MonoBehaviour.</param>
    public void Decay(float stability, float deltaTime)
    {
        float decayRate = 0.007f + stability * 0.003f;
        float factor    = 1f - decayRate * deltaTime * 60f;

        P *= factor;
        A *= factor;
        D *= factor;
    }

    /// <summary>Returns the PAD octant label (e.g. "Exuberant", "Anxious").</summary>
    public string GetOctantName()
    {
        string pSign = P >= 0f ? "+P" : "-P";
        string aSign = A >= 0f ? "+A" : "-A";
        string dSign = D >= 0f ? "+D" : "-D";

        return $"{pSign}{aSign}{dSign}" switch
        {
            "+P+A+D" => "Exuberant",
            "+P+A-D" => "Dependent",
            "+P-A+D" => "Relaxed",
            "+P-A-D" => "Docile",
            "-P+A+D" => "Hostile",
            "-P+A-D" => "Anxious",
            "-P-A+D" => "Disdainful",
            "-P-A-D" => "Bored",
            _        => "Neutral"
        };
    }

    private void Clamp()
    {
        P = Mathf.Clamp(P, -1f, 1f);
        A = Mathf.Clamp(A, -1f, 1f);
        D = Mathf.Clamp(D, -1f, 1f);
    }

    public override string ToString() => $"P={P:F3} A={A:F3} D={D:F3} [{GetOctantName()}]";
}
