# iOS build and device testing — deferred

**Nick's decision, 2026-09-12:** focus on the 12-person Android closed test now.
These iOS tasks remain required before iOS distribution; they do not block Android testing.

Apple App ID/capability and Supabase Apple provider are configured. Native bridge and
Unity post-build signing support exist. No successful iOS build/device test is recorded.
Xcode and Unity iOS Build Support were missing during the implementation checks.

1. [ ] Install Xcode and iOS Build Support for the project's Unity version.
2. [ ] Export an iOS build from Unity; open the generated project in Xcode.
3. [ ] Set signing team/provisioning for `com.nickdegroot.hazardheights`;
   verify Sign in with Apple entitlements and AuthenticationServices framework.
4. [ ] Compile, sign and install on a real iPhone; resolve native/build errors.
5. [ ] Test guest → Apple linking, cancellation, restart and same-account recovery
   after reinstall/second device. Verify the Supabase user ID and synced progress.
6. [ ] Finish Apple grant revocation for account deletion; configure the required
   server credentials and test deletion. Never ship private Apple keys in the app.
7. [ ] Complete Apple Unlimited purchase verification and sandbox purchase/restore tests.
8. [ ] Test layout/safe areas, startup, pause/resume, offline behavior and the iOS ad/consent flow.
9. [ ] Archive/upload to TestFlight, complete required review, and repeat acceptance
   on the distributed build. Record build number, device/iOS version and results.

See [GOLIVE.md](GOLIVE.md) for the full release plan and
[CLOSEDTESTING.md](CLOSEDTESTING.md) for current Android work.
