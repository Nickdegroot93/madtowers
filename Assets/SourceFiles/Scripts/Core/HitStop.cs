using UnityEngine;

/// <summary>
/// Micro time-freeze on big impacts (3-5 frames) - the classic "the hit had weight"
/// trick. Purely Time.timeScale, no physics code touched. Never fights real pauses:
/// refuses to start while the game is paused/slowed, and on restore only writes the
/// timescale back if nothing else (pause menu, picker, slow-mo) changed it meanwhile.
/// </summary>
public sealed class HitStop : MonoBehaviour
{
    private static HitStop _instance;
    private float _restoreAt = -1f;
    private float _stopScale;

    public static void Trigger(float seconds, float scale = 0.05f)
    {
        if (Time.timeScale != 1f) return; // paused, picker open, or slow-mo - stay out

        if (_instance == null)
        {
            _instance = new GameObject("HitStop").AddComponent<HitStop>();
        }
        _instance._stopScale = scale;
        _instance._restoreAt = Time.unscaledTime + seconds;
        Time.timeScale = scale;
    }

    private void Update()
    {
        if (_restoreAt < 0f || Time.unscaledTime < _restoreAt) return;

        // Only restore if the timescale is still OURS - a pause that opened during the
        // stop owns it now and must not be overwritten back to 1. (Restore is always
        // to 1f: the Trigger guard above means a stop can only ever start from 1.)
        if (Mathf.Approximately(Time.timeScale, _stopScale))
        {
            Time.timeScale = 1f;
        }
        _restoreAt = -1f;
    }
}
