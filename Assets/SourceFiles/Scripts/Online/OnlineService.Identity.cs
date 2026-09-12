using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

public partial class OnlineService
{
    public static bool IdentityBusy { get; private set; }
    private static int _requestsInFlight;
    // Authentication can commit before the game RPCs finish. Allow initialization
    // retries for that permanent identity without linking another provider account.
    private static bool _identityProfilePending;

    private sealed class IdentityResult
    {
        public bool ok;
        public string message = "Couldn't finish sign-in. Try again";
    }

    private static void BeginIdentity(string provider, Action<bool, string> done)
    {
        if (IdentityBusy) { done?.Invoke(false, "Sign-in already in progress"); return; }
        if (!NativeIdentity.Supports(provider))
        {
            done?.Invoke(false, "Use a mobile build to sign in");
            return;
        }
        if (!IsReady || _booting || _instance == null || !SupabaseSession.HasSession)
        {
            done?.Invoke(false, "Connect to the internet and try again");
            return;
        }
        if (!SupabaseSession.IsAnonymous && !_identityProfilePending)
        {
            done?.Invoke(false, "This account is already linked");
            return;
        }
        if (PremiumStore.Busy || RunGate.IdentityChangeBlocked || RewardedAds.IsShowing)
        {
            done?.Invoke(false, "Finish the current action first");
            return;
        }
        IdentityBusy = true;
        _instance.StartCoroutine(_instance.IdentityCo(provider, done));
    }

    private IEnumerator IdentityCo(string provider, Action<bool, string> done)
    {
        var result = new IdentityResult();
        try { yield return GuardIdentityCo(ExchangeIdentityCo(provider, result)); }
        finally
        {
            IdentityBusy = false;
            try
            {
                StateChanged?.Invoke();
                if (IsReady)
                {
                    AttemptsSync.ForceRefresh();
                    ProgressSync.OnReady();
                    RunGate.RetryPendingFinishes();
                }
            }
            finally { done?.Invoke(result.ok, result.ok ? null : result.message); }
        }
    }

    // Unity does not propagate a nested coroutine's exception to its parent's catch.
    // Flatten this transaction so unexpected errors still release the busy guard.
    private static IEnumerator GuardIdentityCo(IEnumerator routine)
    {
        var stack = new System.Collections.Generic.Stack<IEnumerator>();
        stack.Push(routine);
        try
        {
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                bool next;
                try { next = current.MoveNext(); }
                catch (Exception)
                {
                    Debug.LogWarning("[Online] Sign-in interrupted. Retry to refresh account state.");
                    yield break;
                }
                if (!next) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (current.Current is IEnumerator child) stack.Push(child);
                else yield return current.Current;
            }
        }
        finally { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); }
    }

    private IEnumerator ExchangeIdentityCo(string provider, IdentityResult result)
    {
        string sourceUser = SupabaseSession.UserId;
        NativeIdentity.Credential credential = null;
        // A committed link may still need its game profile loaded. Retrying that work
        // uses the stored session, without opening another provider account chooser.
        if (SupabaseSession.IsAnonymous)
        {
            NativeIdentity.SignIn(provider, value => credential = value);
            while (credential == null) yield return null;
            if (!string.IsNullOrEmpty(credential.error)) { result.message = credential.error; yield break; }
        }

        // Let all old-account replies finish before exchanging or replacing any session.
        float deadline = Time.realtimeSinceStartup + 30f;
        while (_requestsInFlight > 0 || _refreshInFlight)
        {
            if (Time.realtimeSinceStartup > deadline) yield break;
            yield return null;
        }
        if (sourceUser != SupabaseSession.UserId) yield break;
        if (SupabaseSession.NeedsRefresh)
        {
            RefreshOutcome outcome = RefreshOutcome.NetworkFail;
            yield return RefreshCo(value => outcome = value);
            if (outcome != RefreshOutcome.Ok) yield break;
        }

        AuthResponse auth;
        if (!SupabaseSession.IsAnonymous)
        {
            auth = new AuthResponse {
                access_token = SupabaseSession.AccessToken, refresh_token = SupabaseSession.RefreshToken,
                expires_at = SupabaseSession.ExpiresAtUnixUtc,
                user = new AuthUser { id = sourceUser, is_anonymous = false },
            };
        }
        else
        {
            string authJson = null;
            string errorCode = null;
            yield return NativeTokenCo(provider, credential, true, SupabaseSession.AccessToken,
                (json, code) => { authJson = json; errorCode = code; });
            bool linking = true;
            // A reinstall creates a fresh guest. Only this explicit conflict authorizes
            // signing into a previously linked identity. Other failures preserve the guest.
            if (authJson == null && errorCode == "identity_already_exists")
            {
                linking = false;
                yield return NativeTokenCo(provider, credential, false, null,
                    (json, code) => { authJson = json; errorCode = code; });
            }
            if (authJson == null)
            {
                Debug.LogWarning("[Online] Native sign-in exchange failed (" + (errorCode ?? "network") + ").");
                yield break;
            }
            try { auth = JsonUtility.FromJson<AuthResponse>(authJson); }
            catch { yield break; }
            if (!ValidAuth(auth) || auth.user.is_anonymous ||
                (linking && auth.user.id != sourceUser) || sourceUser != SupabaseSession.UserId) yield break;
        }

        if (credential != null) { credential.idToken = null; credential.nonce = null; }

        bool switching = auth.user.id != sourceUser;
        // Do not abandon earned guest purchases or unfinished ranked reports. A new
        // identity links in place; recovery of a different identity must preserve them.
        if (switching && (PremiumStore.IsPremium || RunGate.HasPendingReports))
        {
            result.message = PremiumStore.IsPremium
                ? "This guest owns Unlimited. Contact support to recover another account"
                : "Sync pending results before recovering this account";
            yield break;
        }

        if (!switching)
        {
            // Supabase already committed this in-place link. Keep its session even
            // if a later game RPC fails; there is no server-side rollback to a guest.
            StoreAuth(auth);
            _identityProfilePending = true;
            result.message = "Signed in. Tap sign in again to finish syncing";
        }

        bool marked = false;
        yield return IdentityRpcCo("mark_linked", "{}", auth.access_token, _ => marked = true);
        if (!marked) yield break;
        string profileJson = null;
        yield return IdentityRpcCo("get_profile", "{}", auth.access_token, json => profileJson = json);
        ProfileDto profile;
        try { profile = JsonUtility.FromJson<ProfileDto>(profileJson ?? ""); }
        catch { yield break; }
        if (profile == null || string.IsNullOrEmpty(profile.display_name) || profile.xp < 0) yield break;

        string attemptsJson = null;
        yield return IdentityRpcCo("get_attempts", "{}", auth.access_token, json => attemptsJson = json);
        if (!AttemptsSync.IsValidSnapshot(attemptsJson)) yield break;

        // Guest progress joins the destination using the existing union/max rules.
        // XP, purchases and ranked scores remain owned by the server-side account.
        string payload = ProgressStore.ExportPayloadJson();
        long mutation = ProgressStore.MutationCounter;
        string merged = null;
        yield return IdentityRpcCo("merge_progress",
            "{\"p_payload\":" + payload + ",\"p_schema_version\":" + ProgressStore.SchemaVersion + "}",
            auth.access_token, json => merged = json);
        if (sourceUser != SupabaseSession.UserId || mutation != ProgressStore.MutationCounter ||
            !ProgressStore.CanApplyMergedPayload(merged)) yield break;

        if (switching)
        {
            // Leave the guest's caches untouched until the new session is durable.
            // A restart between these writes binds the saved owner before boot refresh.
            if (!SupabaseSession.TryStore(auth.access_token, auth.refresh_token, auth.user.id,
                                         AuthExpiresAt(auth), false))
            {
                result.message = "Couldn't save sign-in on this device. Free storage and retry";
                yield break;
            }
            IsLinked = true;
            _identityProfilePending = true;
            ResetAccountState();
            ProgressStore.BindOnlineAccount(auth.user.id);
        }
        if (!ProgressStore.ApplyMergedPayload(merged)) yield break;
        _displayName = profile.display_name;
        IsLinked = true;
        State = OnlineState.Ready;
        XpSystem.ApplyServerTotal(profile.xp);
        AttemptsSync.ApplySnapshot(attemptsJson);
        AttemptsService.ApplyGrantsRemaining(profile.ad_grants_remaining);
        _identityProfilePending = false;
        result.ok = true;
    }

    private static void ResetAccountState()
    {
        RunGate.ClearActiveRun();
        AttemptsSync.OnAccountChanged();
        AttemptsService.OnAccountChanged();
        ProgressSync.OnAccountChanged();
    }

    private static bool ValidAuth(AuthResponse auth) =>
        auth != null && !string.IsNullOrEmpty(auth.access_token) &&
        !string.IsNullOrEmpty(auth.refresh_token) && auth.user != null &&
        Guid.TryParse(auth.user.id, out _);

    private static IEnumerator NativeTokenCo(string provider, NativeIdentity.Credential credential,
                                             bool link, string bearer, Action<string, string> done)
    {
        var body = new JObject {
            ["provider"] = provider, ["id_token"] = credential.idToken,
            ["nonce"] = credential.nonce, ["link_identity"] = link,
        };
        using (var request = SupabaseHttp.AuthPost("/auth/v1/token?grant_type=id_token",
                                                   body.ToString(Newtonsoft.Json.Formatting.None), bearer))
        {
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
                done(request.downloadHandler.text, null);
            else
            {
                string code = null;
                try { code = (string)JObject.Parse(request.downloadHandler.text)["error_code"]; }
                catch { /* Do not echo tokens or provider response bodies. */ }
                done(null, code);
            }
        }
    }

    private static IEnumerator IdentityRpcCo(string fn, string body, string bearer, Action<string> done)
    {
        using (var request = SupabaseHttp.AuthPost("/rest/v1/rpc/" + fn, body, bearer))
        {
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) done(request.downloadHandler.text);
        }
    }
}
