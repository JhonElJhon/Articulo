using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Handles high-speed logging of Agent reactions to a CSV file.
/// Writes to the root folder of the Unity project without blocking the main simulation thread unnecessarily.
/// </summary>
public class DataLogger : MonoBehaviour
{
    public static DataLogger Instance;
    
    private string filePath;
    private StreamWriter writer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        // Write to project root, not inside Assets/ to avoid excessive Unity re-imports
        filePath = Path.Combine(Application.dataPath, "..", "SimulationLogs.csv");
        Debug.Log(filePath);
        
        try
        {
            writer = new StreamWriter(filePath, false, Encoding.UTF8); // Overwrite old logs on restart
            
            // Write CSV Header
            writer.WriteLine("AgentID;AgentName;Team;Personality;Openness;Conscientiousness;Extraversion;Agreeableness;Neuroticism;Stability;" +
                             "PrevMood_P;PrevMood_A;PrevMood_D;" +
                             "EventID;Emotion;EmotionIntensity;" +
                             "NewMood_P;NewMood_A;NewMood_D;" +
                             "Action;" +
                             "Time");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DataLogger] Failed to open file at {filePath}: {e.Message}");
        }
    }

    /// <summary>
    /// Appends a single row to the CSV file representing an agent's reaction.
    /// </summary>
    public void LogReaction(
        int agentId, string agentName, string team, OCEAN_Model.Frames personality,
        double o, double c, double e, double a, double n, double s,
        PADValues prevMood, 
        string eventId, string emotion, float emotionIntensity,
        PADValues newMood, 
        string action,
        float timer)
    {
        if (writer == null) return;
        
        // Format to CSV string safely
        string line = string.Format(
            "{0};{1};{2};{3};{4:F3};{5:F3};{6:F3};{7:F3};{8:F3};{9:F3};{10:F3};{11:F3};{12:F3};{13};{14};{15:F3};{16:F3};{17:F3};{18:F3};{19};{20:F3}",
            //"{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19}",
            agentId, agentName, team, personality,
            o, c, e, a, n, s,
            prevMood.P, prevMood.A, prevMood.D,
            eventId, emotion, emotionIntensity,
            newMood.P, newMood.A, newMood.D,
            action,
            timer
        );

        //line = line.Replace(',', '.');
        writer.WriteLine(line);
    }

    private void OnDestroy()
    {
        if (writer != null)
        {
            writer.Flush();
            writer.Close();
        }
    }
}
