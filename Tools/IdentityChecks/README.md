# Native identity checks

Run `python3 Tools/IdentityChecks/run.py` from the repository. On macOS the runner finds
the installed project Unity version under `~/Unity`; set `UNITY_APP` to a different
`Unity.app` path when needed. It uses Unity's Roslyn compiler and Mono runtime and the
project's Newtonsoft DLL. All session files are created in a temporary directory.

The harness compiles the actual `OnlineService`, `OnlineService.Identity`,
`SupabaseHttp`, and `SupabaseSession` with fake native UI, network and game services.
It checks linking in place, identity-conflict recovery, explicit bearer ownership,
nonce submission, cancellation, duplicate requests, malformed/wrong-account replies,
failed preflight, save mutation during recovery, merge rejection, guest entitlements,
pending ranked results, timeout, nested exceptions, and durable session-write failure.
The second fixture, `RecoveryChecks.cs.txt`, compiles the production ProgressStore,
AttemptsSync, AttemptsService and XpSystem alongside the auth classes. It checks failed
session writes preserve the exact guest save, retries after storage recovers, mismatched
caches on offline restart, partial linking and lost auth responses, initialization retry
without native UI, refill resets after recovery/deletion, and stale refill polling.
Both fixtures still substitute Unity scheduling/JSON and the native/network boundary.
They do not prove Google/Apple's native UI or device signing.

Implementation uses Credential Manager 1.6.0 + Google ID 1.1.1 on Android, and
AuthenticationServices directly on iOS. Android dependencies are declared in the
Gradle template and EDM4U XML; the iOS build hook adds the framework and entitlement.
Nonce hashes go to providers; raw nonces go to Supabase. No provider secrets ship.

Recovery policy: link a new identity to the guest in place and immediately retain the
committed auth session. If subsequent game RPCs fail, retry initialization using that
session. Ordinary refresh also reconciles a link whose original response was lost.
If Supabase returns
`identity_already_exists`, sign in to that provider account and preflight profile,
attempts and the existing union/max progress merge before replacing the session.
Persist a different account's session before clearing the guest caches, so a failed
write preserves the guest. Boot binds cache ownership before attempting token refresh,
including when that refresh fails offline.
Guest purchases and pending ranked reports block changing to a different account;
sync pending reports first, or handle purchased-guest recovery through support.
Only guest accounts can use this flow; changing or linking multiple permanent
accounts is outside this first integration. Android-to-iOS recovery is not supported.
Account-owned premium/XP/meter caches are reset on account changes. Pending ranked
reports live in account-specific files, with migration from the earlier single file.

Device acceptance (disposable accounts, internal testing first):

1. Install a freshly built signed AAB through Google Play. For sideloaded builds,
   register an additional Android OAuth client for that build's signing SHA-1.
2. Earn guest progress, sign in with Google, restart and verify progress/name.
3. Cancel login and interrupt networking during login; the guest must remain usable.
4. After confirmed cloud sync, reinstall the QA build and recover the same account.
5. Repeat on iPhone using a signed build with the Sign in with Apple entitlement.
6. Verify account deletion, including Apple's token-revocation requirements, before
   approving iOS acceptance. The existing data-deletion RPC does not revoke Apple grants;
   native-only Supabase configuration does not supply Apple's revocation credentials.

2026-09-12 fix validation: **166 assertions passed** (130 transaction checks and 36
checks using real save/cache services). Editor and Android C# compilation passed using
Unity's compiler; Java bridge compiled against the actual Credential Manager and
Google ID libraries during initial implementation. iOS native compilation/device acceptance remain unverified:
this Mac has neither Xcode nor the Unity iOS Build Support module installed.

Android device result, 2026-09-12: Nick reports testing works after submitting
2.1.2 / code 3 to Play closed testing and authorizes commit/push. Individual scenario
results and device details were not supplied. iOS testing remains deferred.
