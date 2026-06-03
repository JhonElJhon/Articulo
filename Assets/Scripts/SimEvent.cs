using UnityEngine;

// ── Supporting enums ──────────────────────────────────────────────────────────

/// <summary>Global events reach all agents; Local events use a proximity radius.</summary>
public enum EventScope { Global, Local }

/// <summary>
/// Who caused the event — key to the OCC appraisal branch.
/// Same event feels different if caused by an enemy vs. the game itself.
/// </summary>
public enum EventAgency { Game, Self, Ally, Enemy }

// ─────────────────────────────────────────────────────────────────────────────
// SimEvent
// ─────────────────────────────────────────────────────────────────────────────
/// <summary>
/// Represents something that happened in the simulation.
/// Events carry APPRAISAL SIGNALS, NOT pre-assigned emotions.
/// Each agent reads the signals and decides for itself what to feel,
/// based on its own personality, mood, and goals.
///
/// Convention for desirability:
///   Values are from the INSTIGATING TEAM's perspective.
///   Agents on the OTHER team flip the sign when appraising.
/// </summary>
[System.Serializable]
public struct SimEvent
{
    public string     eventId;
    public EventScope scope;
    public EventAgency agency;

    /// <summary>Team whose fans consider this event positive.</summary>
    public Agent.Teams instigatingTeam;

    /// <summary>Agent who triggered this event (null for game events).</summary>
    public Agent instigator;

    /// <summary>Targeted agent (null for non-targeted events).</summary>
    public Agent target;

    // ── Appraisal Signals ─────────────────────────────────────────────────────
    /// <summary>How good/bad is this from instigating team's view? [-1, 1]</summary>
    public float desirability;

    /// <summary>How unexpected? [0, 1] — amplifies arousal and intensity.</summary>
    public float suddenness;

    /// <summary>How visible and shared with the crowd? [0, 1] — amplifies social agents.</summary>
    public float socialWeight;

    /// <summary>Physical danger level. [0, 1] — triggers Fear/Anger split.</summary>
    public float physicalThreat;

    /// <summary>Moral valence of the action. [-1, 1] — drives Admiration/Reproach.</summary>
    public float praiseworthiness;

    // ─────────────────────────────────────────────────────────────────────────
    // Factory methods — one for each game/agent action
    // ─────────────────────────────────────────────────────────────────────────

    // ── Global Events ──────────────────────────────────────────────────────────

    public static SimEvent TeamScores(Agent.Teams scoringTeam) => new SimEvent
    {
        eventId           = "teamScores",
        scope             = EventScope.Global,
        agency            = EventAgency.Game,
        instigatingTeam   = scoringTeam,
        desirability      =  0.85f,
        suddenness        =  0.45f,
        socialWeight      =  1.00f,
        physicalThreat    =  0.00f,
        praiseworthiness  =  0.00f
    };

    public static SimEvent NearMiss(Agent.Teams attemptingTeam) => new SimEvent
    {
        eventId           = "nearMiss",
        scope             = EventScope.Global,
        agency            = EventAgency.Game,
        instigatingTeam   = attemptingTeam,
        desirability      =  0.30f,
        suddenness        =  0.85f,  // very surprising
        socialWeight      =  0.80f,
        physicalThreat    =  0.00f,
        praiseworthiness  =  0.00f
    };

    public static SimEvent ControversialCall(Agent.Teams disadvantagedTeam) => new SimEvent
    {
        eventId           = "controversialCall",
        scope             = EventScope.Global,
        agency            = EventAgency.Game,
        instigatingTeam   = disadvantagedTeam,
        desirability      = -0.55f,
        suddenness        =  0.55f,
        socialWeight      =  0.90f,
        physicalThreat    =  0.00f,
        praiseworthiness  = -0.60f   // the call was unfair
    };

    /// <summary>
    /// Mid-game score check. Desirability scales with score differential.
    /// scoreDifferential > 0 means instigatingTeam is ahead.
    /// </summary>
    public static SimEvent GameSummary(Agent.Teams favoredTeam, float scoreDifferential) => new SimEvent
    {
        eventId           = "gameSummary",
        scope             = EventScope.Global,
        agency            = EventAgency.Game,
        instigatingTeam   = favoredTeam,
        desirability      =  Mathf.Clamp(scoreDifferential * 0.25f, -0.75f, 0.75f),
        suddenness        =  0.10f,
        socialWeight      =  0.60f,
        physicalThreat    =  0.00f,
        praiseworthiness  =  0.00f
    };

    public static SimEvent GameEnd(Agent.Teams winningTeam, float margin) => new SimEvent
    {
        eventId           = "gameEnd",
        scope             = EventScope.Global,
        agency            = EventAgency.Game,
        instigatingTeam   = winningTeam,
        desirability      =  Mathf.Clamp(0.60f + margin * 0.08f, 0.60f, 0.95f),
        suddenness        =  0.20f,
        socialWeight      =  1.00f,
        physicalThreat    =  0.00f,
        praiseworthiness  =  0.00f
    };

    // ── Local Events ───────────────────────────────────────────────────────────

    public static SimEvent WatchCalmly(Agent source) => new SimEvent
    {
        eventId = "watchCalmly",
        scope = EventScope.Local,
        agency = EventAgency.Ally,
        instigatingTeam = source.team,
        instigator = source,
        desirability = 0f,
        suddenness = 0f,
        socialWeight = 0f,
        physicalThreat = 0f,
        praiseworthiness = 0f
    };

    public static SimEvent Celebration(Agent source) => new SimEvent
    {
        eventId           = "celebration",
        scope             = EventScope.Local,
        agency            = EventAgency.Ally,
        instigatingTeam   = source.team,
        instigator        = source,
        desirability      =  0.55f,
        suddenness        =  0.25f,
        socialWeight      =  0.70f,
        physicalThreat    =  0.00f,
        praiseworthiness  =  0.40f
    };

    public static SimEvent Chant(Agent source) => new SimEvent
    {
        eventId           = "chant",
        scope             = EventScope.Local,
        agency            = EventAgency.Ally,
        instigatingTeam   = source.team,
        instigator        = source,
        desirability      =  0.40f,
        suddenness        =  0.10f,
        socialWeight      =  0.90f,  // very social, rallying
        physicalThreat    =  0.00f,
        praiseworthiness  =  0.20f
    };

    public static SimEvent Boo(Agent source) => new SimEvent
    {
        eventId = "boo",
        scope = EventScope.Local,
        agency = EventAgency.Enemy,
        instigatingTeam = source.team,
        instigator = source,
        desirability = -0.40f,
        suddenness = 0.10f,
        socialWeight = 0.90f,  // very social, rallying
        physicalThreat = 0.00f,
        praiseworthiness = -0.20f
    };

    public static SimEvent Insult(Agent source, Agent targetAgent) => new SimEvent
    {
        eventId           = "insult",
        scope             = EventScope.Local,
        agency            = EventAgency.Enemy,
        instigatingTeam   = source.team,
        instigator        = source,
        target            = targetAgent,
        desirability      = -0.50f,
        suddenness        =  0.50f,
        socialWeight      =  0.45f,
        physicalThreat    =  0.10f,
        praiseworthiness  = -0.80f
    };

    public static SimEvent Fight(Agent source, Agent targetAgent) => new SimEvent
    {
        eventId           = "fight",
        scope             = EventScope.Local,
        agency            = EventAgency.Enemy,
        instigatingTeam   = source.team,
        instigator        = source,
        target            = targetAgent,
        desirability      = -0.80f,
        suddenness        =  0.70f,
        socialWeight      =  0.70f,
        physicalThreat    =  0.85f,  // very dangerous for nearby agents
        praiseworthiness  = -0.90f
    };

    public static SimEvent Run(Agent source) => new SimEvent
    {
        eventId = "run",
        scope = EventScope.Local,
        agency = EventAgency.Ally,
        instigatingTeam = source.team,
        instigator = source,
        desirability = 0f,
        suddenness = 0.9f,
        socialWeight = 0.50f,
        physicalThreat = 0.00f,
        praiseworthiness = 0f
    };

    public static SimEvent ComfortAlly(Agent source, Agent targetAgent) => new SimEvent
    {
        eventId           = "comfort",
        scope             = EventScope.Local,
        agency            = EventAgency.Ally,
        instigatingTeam   = source.team,
        instigator        = source,
        target            = targetAgent,
        desirability      =  0.35f,
        suddenness        =  0.10f,
        socialWeight      =  0.20f,
        physicalThreat    =  0.00f,
        praiseworthiness  =  0.70f
    };

    public static SimEvent CalmSituation(Agent source, Agent targetAgent) => new SimEvent
    {
        eventId = "calmSituation",
        scope = EventScope.Local,
        agency = EventAgency.Enemy,
        instigatingTeam = source.team,
        instigator = source,
        target = targetAgent,
        desirability = 0.35f,
        suddenness = 0.50f,
        socialWeight = 0.45f,
        physicalThreat = 0.00f,
        praiseworthiness = 0.80f
    };

    /// <summary>
    /// A fan invades the pitch. Highly visible — broadcast at wide radius.
    /// Positive for same team fans (thrilling), threatening for opposing fans.
    /// </summary>
    public static SimEvent PitchInvasion(Agent source) => new SimEvent
    {
        eventId           = "pitchInvasion",
        scope             = EventScope.Local,
        agency            = EventAgency.Self,
        instigatingTeam   = source.team,
        instigator        = source,
        desirability      =  0.75f,
        suddenness        =  0.90f,
        socialWeight      =  1.00f,
        physicalThreat    =  0.40f,   // chaos increases threat for all
        praiseworthiness  = -0.30f    // exciting but rule-breaking
    };

    public static SimEvent ThrowObject(Agent source) => new SimEvent
    {
        eventId           = "throwObject",
        scope             = EventScope.Local,
        agency            = EventAgency.Enemy,
        instigatingTeam   = source.team,
        instigator        = source,
        desirability      = -0.60f,
        suddenness        =  0.80f,
        socialWeight      =  0.65f,
        physicalThreat    =  0.55f,
        praiseworthiness  = -0.90f
    };

    public static SimEvent FormGroup(Agent source) => new SimEvent
    {
        eventId           = "formGroup",
        scope             = EventScope.Local,
        agency            = EventAgency.Ally,
        instigatingTeam   = source.team,
        instigator        = source,
        desirability      =  0.40f,
        suddenness        =  0.10f,
        socialWeight      =  0.80f,
        physicalThreat    =  0.00f,
        praiseworthiness  =  0.30f
    };
}
