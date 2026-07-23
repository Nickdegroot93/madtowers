using System;
using System.Collections.Generic;

/// <summary>
/// Leaderboard reads (BACKEND.md §7): one RPC returns the top-N plus the caller's own row
/// and rank for a level+board. Scores are written only by finish_run server-side - there is
/// deliberately no submit API here.
/// </summary>
public static class Leaderboards
{
    // Field names mirror get_leaderboard's jsonb keys exactly (JsonUtility maps by name).
    // The row's boosted loadout is a free-form jsonb the server also sends; JsonUtility
    // can't represent it, so it is deliberately absent - the BOOSTED tab itself is the badge.
    [Serializable]
    public class Entry
    {
        public int rank;
        public string display_name;
        public bool is_linked;
        public int best_score;
        public float best_height;
        public bool is_you;
    }

    [Serializable]
    public class LeaderboardResult
    {
        public List<Entry> entries;

        /// <summary>The caller's row (rank may exceed the page; carries no name - the client
        /// knows its own). Null when they have no score on this board yet.</summary>
        public Entry you;
    }

    public static void Fetch(string levelId, bool boosted,
                             Action<LeaderboardResult> onOk, Action<string> onErr)
    {
        string body = $"{{\"p_level_id\":\"{SupabaseHttp.JsonEscape(levelId)}\"," +
                      $"\"p_board\":\"{(boosted ? "boosted" : "clean")}\",\"p_limit\":100}}";
        OnlineService.RpcObject<LeaderboardResult>("get_leaderboard", body,
            result =>
            {
                result.entries ??= new List<Entry>();
                // JsonUtility materializes a default instance for JSON null; rank 0 = no row.
                if (result.you != null && result.you.rank <= 0) result.you = null;
                onOk?.Invoke(result);
            },
            onErr);
    }
}
