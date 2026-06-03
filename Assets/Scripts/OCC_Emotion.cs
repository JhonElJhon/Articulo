using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// PAD Values: the three-dimensional emotional space (Mehrabian 1974)
// ─────────────────────────────────────────────────────────────────────────────
[System.Serializable]
public struct PADValues
{
    public float P; // Pleasure   [-1, 1]
    public float A; // Arousal    [-1, 1]
    public float D; // Dominance  [-1, 1]

    public PADValues(float p, float a, float d) { P = p; A = a; D = d; }

    public static PADValues Zero => new PADValues(0f, 0f, 0f);

    public float Magnitude => Mathf.Sqrt(P * P + A * A + D * D);

    /// <summary>
    /// Cosine similarity between two PAD vectors. Used by BDI for action scoring.
    /// Returns values in [-1, 1]; 1 = perfectly aligned, -1 = opposite.
    /// </summary>
    public static float CosineSimilarity(PADValues a, PADValues b)
    {
        float dot  = a.P * b.P + a.A * b.A + a.D * b.D;
        float magA = a.Magnitude;
        float magB = b.Magnitude;
        if (magA < 0.0001f || magB < 0.0001f) return 0f;
        return dot / (magA * magB);
    }

    public override string ToString() => $"P={P:F3} A={A:F3} D={D:F3}";
}

// ─────────────────────────────────────────────────────────────────────────────
// OCC Emotion Enum — all 22 Ortony-Clore-Collins emotions
// ─────────────────────────────────────────────────────────────────────────────
public enum OCCEmotion
{
    None,
    Admiration, Anger, Disliking, Disappointment, Distress,
    Fear, FearsConfirmed, Gloating, Gratification, Gratitude,
    HappyFor, Hate, Hope, Joy, Liking, Love, Pity, Pride,
    Relief, Remorse, Reproach, Resentment, Satisfaction, Shame
}

// ─────────────────────────────────────────────────────────────────────────────
// EmotionInstance: an OCC emotion with intensity and its scaled PAD values
// ─────────────────────────────────────────────────────────────────────────────
[System.Serializable]
public struct EmotionInstance
{
    public OCCEmotion emotion;
    public float      intensity; // [0, 1]
    public PADValues  pad;       // Base PAD * intensity + personality modifier

    public static EmotionInstance None => new EmotionInstance
    {
        emotion   = OCCEmotion.None,
        intensity = 0f,
        pad       = PADValues.Zero
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Static PAD lookup table for all OCC emotions (from Mehrabian 1974 mapping)
// ─────────────────────────────────────────────────────────────────────────────
public static class OCCEmotionData
{
    private static readonly Dictionary<OCCEmotion, PADValues> _table =
        new Dictionary<OCCEmotion, PADValues>
        {
            { OCCEmotion.Admiration,     new PADValues( 0.50f,  0.30f, -0.20f) },
            { OCCEmotion.Anger,          new PADValues(-0.51f,  0.59f,  0.25f) },
            { OCCEmotion.Disliking,      new PADValues(-0.40f,  0.20f,  0.10f) },
            { OCCEmotion.Disappointment, new PADValues(-0.30f,  0.10f, -0.40f) },
            { OCCEmotion.Distress,       new PADValues(-0.40f, -0.20f, -0.50f) },
            { OCCEmotion.Fear,           new PADValues(-0.64f,  0.60f, -0.43f) },
            { OCCEmotion.FearsConfirmed, new PADValues(-0.50f, -0.30f, -0.70f) },
            { OCCEmotion.Gloating,       new PADValues( 0.30f, -0.30f, -0.10f) },
            { OCCEmotion.Gratification,  new PADValues( 0.60f,  0.50f,  0.40f) },
            { OCCEmotion.Gratitude,      new PADValues( 0.40f,  0.20f, -0.30f) },
            { OCCEmotion.HappyFor,       new PADValues( 0.40f,  0.20f,  0.20f) },
            { OCCEmotion.Hate,           new PADValues(-0.60f,  0.60f,  0.30f) },
            { OCCEmotion.Hope,           new PADValues( 0.20f,  0.20f, -0.10f) },
            { OCCEmotion.Joy,            new PADValues( 0.40f,  0.20f,  0.10f) },
            { OCCEmotion.Liking,         new PADValues( 0.40f,  0.16f, -0.24f) },
            { OCCEmotion.Love,           new PADValues( 0.30f,  0.10f,  0.20f) },
            { OCCEmotion.Pity,           new PADValues(-0.40f, -0.20f, -0.50f) },
            { OCCEmotion.Pride,          new PADValues( 0.40f,  0.30f,  0.30f) },
            { OCCEmotion.Relief,         new PADValues( 0.20f, -0.30f,  0.40f) },
            { OCCEmotion.Remorse,        new PADValues(-0.30f,  0.10f, -0.60f) },
            { OCCEmotion.Reproach,       new PADValues(-0.30f, -0.10f,  0.40f) },
            { OCCEmotion.Resentment,     new PADValues(-0.20f, -0.30f, -0.20f) },
            { OCCEmotion.Satisfaction,   new PADValues( 0.30f, -0.20f,  0.40f) },
            { OCCEmotion.Shame,          new PADValues(-0.30f,  0.10f, -0.60f) },
        };

    public static PADValues GetPAD(OCCEmotion emotion) =>
        _table.TryGetValue(emotion, out PADValues v) ? v : PADValues.Zero;

    /// <summary>
    /// Finds the OCC emotion whose PAD coordinates are closest to the target.
    /// Used to label mood states and debug.
    /// </summary>
    public static OCCEmotion FindClosest(PADValues target)
    {
        OCCEmotion best    = OCCEmotion.None;
        float      bestDist = float.MaxValue;

        foreach (var kv in _table)
        {
            float dist = Vector3.Distance(
                new Vector3(target.P, target.A, target.D),
                new Vector3(kv.Value.P, kv.Value.A, kv.Value.D));
            if (dist < bestDist) { bestDist = dist; best = kv.Key; }
        }
        return best;
    }
}
