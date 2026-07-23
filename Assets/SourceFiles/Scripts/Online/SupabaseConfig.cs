/// <summary>
/// Supabase endpoint + public key (BACKEND.md §2). Code-owned statics on purpose - config
/// ScriptableObjects go stale in serialized defaults, and none of this is secret: the anon
/// key is public-by-design (Row-Level Security and SECURITY DEFINER functions do all the
/// guarding server-side; the service-role key must NEVER ship, BACKEND.md §4.3).
///
/// These are the PRODUCTION values (hosted project, schema pushed + smoke-tested 18/18 on
/// 2026-07-23). For local-stack work (`Tools/bin/supabase start`), swap in the local pair
/// below - and remember the port-shifted 55321 (TradeParley owns the 54321 defaults).
/// </summary>
public static class SupabaseConfig
{
    public const string Url = "https://cyinvljdxpdtynlkiqhm.supabase.co";

    public const string AnonKey =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
        "eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImN5aW52bGpkeHBkdHlubGtpcWhtIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODQ4MTg0OTgsImV4cCI6MjEwMDM5NDQ5OH0." +
        "hQBID0H_tDO3hSLPyQfLE03AbpuVAWvI9lwg-KQJPv8";

    // Local dev stack (supabase start):
    // public const string Url = "http://127.0.0.1:55321";
    // public const string AnonKey =
    //     "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
    //     "eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6ImFub24iLCJleHAiOjE5ODM4MTI5OTZ9." +
    //     "CRXP1A7WOeoJeXxjNni43kdQwgnWNReilDMblYTn_I0";

    /// <summary>Master switch for the whole online layer. False = every online service is
    /// inert and the game degrades to the pre-online local behaviour (offline editor work,
    /// BACKEND.md §5 scope note). Non-serialized static per the staleness rule.</summary>
    public static bool Enabled = true;
}
