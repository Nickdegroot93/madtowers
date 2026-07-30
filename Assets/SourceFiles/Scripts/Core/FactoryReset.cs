using System.IO;
using UnityEngine;

/// <summary>
/// The "start over on this device" reset (Settings > Account): restores the exact state of a
/// fresh install so a playtest can begin from nothing - tutorial replays, every level locks,
/// wallet/premium/discoveries/attempts gone, settings back to defaults, and the NEXT boot
/// signs up a brand-new anonymous account.
///
/// Dropping the online session together with the save is load-bearing: a save wipe alone
/// would be undone on the next sync, because the server merge (union/max, BACKEND.md §5)
/// would pour the old account's completions straight back into the empty document.
///
/// Ends by quitting the app: every runtime system caches save state in statics (settings
/// channels, coin HUD, attempts meter, online boot), and a relaunch is the one honest way to
/// rebuild them all from the wiped disk. In the editor it stops play mode instead.
/// </summary>
public static class FactoryReset
{
    public static void EraseAllAndQuit()
    {
        // Progress document: completions, bests, tutorial flag, discoveries, wallet,
        // attempts meter, premium - the account-deletion wipe spares nothing.
        ProgressStore.WipeForAccountDeletion();

        // Settings, HUD layout, the unlock-reveal one-shot, any legacy keys.
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        // Identity + the offline finish queue (stale run receipts must not follow the
        // new account).
        SupabaseSession.Clear();
        TryDeletePersistentFile("pending_finish.json");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void TryDeletePersistentFile(string fileName)
    {
        try
        {
            string path = Path.Combine(Application.persistentDataPath, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best effort - a leftover queue file cannot brick the fresh start */ }
    }
}
