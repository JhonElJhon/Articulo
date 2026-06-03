using UnityEngine;

/// <summary>
/// Stores a single agent's OCEAN (Big Five) personality profile.
///
/// Constructor takes Stability (S = 1 − N) as the fifth parameter,
/// and derives Neuroticism automatically.
/// This keeps the frame definitions clean: "high stability" means passing a high
/// value as s, which correctly yields low N = 1 − s.
///
/// All values are in [0, 1].
/// </summary>
[System.Serializable]
public class OCEAN_Model
{
    public double Openness         { get; set; }
    public double Conscientiousness{ get; set; }
    public double Extraversion     { get; set; }
    public double Agreeableness    { get; set; }
    public double Stability        { get; set; }  // S = 1 − N
    public double Neuroticism      { get; set; }  // N = 1 − S  (derived)

    /// <param name="o">Openness</param>
    /// <param name="c">Conscientiousness</param>
    /// <param name="e">Extraversion</param>
    /// <param name="a">Agreeableness</param>
    /// <param name="s">Stability (1 − Neuroticism)</param>
    public OCEAN_Model(double o, double c, double e, double a, double s)
    {
        Openness          = o;
        Conscientiousness = c;
        Extraversion      = e;
        Agreeableness     = a;
        Stability         = s;
        Neuroticism       = 1.0 - s; // derived: N = 1 − S
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Random generation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>All five traits uniformly distributed in [0, 1].</summary>
    public static OCEAN_Model GenerateRandom() => new OCEAN_Model(
        Random.Range(0f, 1f),
        Random.Range(0f, 1f),
        Random.Range(0f, 1f),
        Random.Range(0f, 1f),
        Random.Range(0f, 1f));

    // ─────────────────────────────────────────────────────────────────────────
    // Personality frames
    // Based on van Mensvoort's OCEAN frame taxonomy (see attached table image).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// All 20 frames from the OCEAN personality taxonomy.
    /// The bracketed letters indicate which two traits are [highest, lowest].
    /// For example, Antisocial [OA] has highest O and lowest A.
    /// </summary>
    public enum Frames
    {
        Random,

        // Cluster 1A — Antagonism-based
        Paranoid,           // [CA]  high C,  low A
        Schizoid,           // [SE]  high S,  low E
        Schizotypal,        // [OE]  high O,  low E

        // Cluster 2A — Negative emotional + disinhibition
        Antisocial,         // [OA]  high O,  low A
        Borderline,         // [OS]  high O,  low S   (→ high N)
        Histrionic,         // [EC]  high E,  low C
        Narcissistic,       // [EA]  high E,  low A

        // Cluster 3A — Anxious / inhibited
        Avoidant,           // [CS]  high C,  low S   (→ high N)
        Dependent,          // [AS]  high A,  low S   (→ high N)
        Obsessive_Compulsive,// [CO] high C,  low O

        // Cluster 1B — Positive social
        Pronoid,            // [AC]  high A,  low C
        People_person,      // [ES]  high E,  low S   (→ high N, but socially open)
        Sensible,           // [EO]  high E,  low O

        // Cluster 2B — Prosocial / conventional
        Prosocial,          // [AO]  high A,  low O
        Straightforward,    // [SO]  high S,  low O   (→ low N)
        Non_theatrical,     // [CE]  high C,  low E
        Unpretentious,      // [AE]  high A,  low E

        // Cluster 3B — Stable / relaxed
        Accommodating,      // [SC]  high S,  low C   (→ low N)
        Independent,        // [SA]  high S,  low A   (→ low N, but disagreeable)
        Laissez_faire       // [OC]  high O,  low C
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Frame generation
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Generates a personality with the correct trait ordering for the given frame.
    ///
    /// How it works:
    ///   • highest and lowest are randomly sampled so the full [0,1] range is used.
    ///   • trait1/2/3 fall between lowest and highest, preserving the relative order
    ///     while still giving each agent unique values within their frame.
    ///
    /// Note: Because highest is itself random in [0,1], two Antisocial agents
    /// might both have their max at O=0.9 and O=0.4. The frame determines
    /// relative order, not absolute intensity.
    ///
    /// Column order for the switch statement: O, C, E, A, S (Stability)
    /// </summary>
    public static OCEAN_Model GenerateByFrame(Frames frame)
    {
        if (frame == Frames.Random) return GenerateRandom();

        double highest = Random.Range(0.35f, 1.00f);           // ensure a meaningful peak
        double lowest  = Random.Range(0.00f, (float)highest * 0.65f); // meaningful trough
        double t1      = Random.Range((float)lowest, (float)highest);
        double t2      = Random.Range((float)lowest, (float)highest);
        double t3      = Random.Range((float)lowest, (float)highest);

        // Switch columns:  O        C        E        A        S(tability)
        return frame switch
        {
            Frames.Paranoid            => new OCEAN_Model(t1,      highest, t2,      lowest,  t3),
            Frames.Schizoid            => new OCEAN_Model(t1,      t2,      lowest,  t3,      highest),
            Frames.Schizotypal         => new OCEAN_Model(highest, t1,      lowest,  t2,      t3),
            Frames.Antisocial          => new OCEAN_Model(highest, t1,      t2,      lowest,  t3),
            Frames.Borderline          => new OCEAN_Model(highest, t1,      t2,      t3,      lowest),
            Frames.Histrionic          => new OCEAN_Model(t1,      lowest,  highest, t2,      t3),
            Frames.Narcissistic        => new OCEAN_Model(t1,      t2,      highest, lowest,  t3),
            Frames.Avoidant            => new OCEAN_Model(t1,      highest, t2,      t3,      lowest),
            Frames.Dependent           => new OCEAN_Model(t1,      t2,      t3,      highest, lowest),
            Frames.Obsessive_Compulsive=> new OCEAN_Model(lowest,  highest, t1,      t2,      t3),
            Frames.Pronoid             => new OCEAN_Model(t1,      lowest,  t2,      highest, t3),
            Frames.People_person       => new OCEAN_Model(t1,      t2,      highest, t3,      lowest),
            Frames.Sensible            => new OCEAN_Model(lowest,  t1,      highest, t2,      t3),
            Frames.Prosocial           => new OCEAN_Model(lowest,  t1,      t2,      highest, t3),
            Frames.Straightforward     => new OCEAN_Model(lowest,  t1,      t2,      t3,      highest),
            Frames.Non_theatrical      => new OCEAN_Model(t1,      highest, lowest,  t2,      t3),
            Frames.Unpretentious       => new OCEAN_Model(t1,      t2,      lowest,  highest, t3),
            Frames.Accommodating       => new OCEAN_Model(t1,      lowest,  t2,      t3,      highest),
            Frames.Independent         => new OCEAN_Model(t1,      t2,      t3,      lowest,  highest),
            Frames.Laissez_faire       => new OCEAN_Model(highest, lowest,  t1,      t2,      t3),
            _                          => GenerateRandom()
        };
    }
}
