package com.nickdegroot.hazardheights.auth;

import android.app.Activity;
import android.os.CancellationSignal;
import androidx.annotation.Keep;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialException;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.NoCredentialException;
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import com.unity3d.player.UnityPlayer;
import org.json.JSONObject;

/** Credential Manager button flow; the ID token is verified by Supabase, never locally. */
@Keep
public final class GoogleIdentity {
    private static CancellationSignal pending;

    @Keep
    public static void signIn(Activity activity, String host, String requestId,
                              String clientId, String nonceHash) {
        activity.runOnUiThread(() -> {
            if (pending != null) pending.cancel();
            CancellationSignal signal = new CancellationSignal();
            pending = signal;
            try {
                GetSignInWithGoogleOption option = new GetSignInWithGoogleOption.Builder(clientId)
                        .setNonce(nonceHash).build();
                GetCredentialRequest request = new GetCredentialRequest.Builder()
                        .addCredentialOption(option).build();
                CredentialManager.create(activity).getCredentialAsync(activity, request, signal,
                        activity::runOnUiThread,
                        new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                    @Override public void onResult(GetCredentialResponse response) {
                        if (pending != signal) return;
                        pending = null;
                        try {
                            Credential credential = response.getCredential();
                            if (!(credential instanceof CustomCredential) ||
                                !GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
                                reply(host, requestId, null, "Unexpected sign-in credential");
                                return;
                            }
                            String token = GoogleIdTokenCredential.createFrom(credential.getData()).getIdToken();
                            reply(host, requestId, token, null);
                        } catch (Exception e) {
                            reply(host, requestId, null, "Couldn't read Google sign-in. Try again");
                        }
                    }
                    @Override public void onError(GetCredentialException error) {
                        if (pending != signal) return;
                        pending = null;
                        String message = error instanceof GetCredentialCancellationException ? "Sign-in cancelled"
                                : error instanceof NoCredentialException ? "Add a Google account on this device"
                                : "Google sign-in failed. Try again";
                        reply(host, requestId, null, message);
                    }
                });
            } catch (Exception e) {
                if (pending == signal) pending = null;
                reply(host, requestId, null, "Couldn't open Google sign-in. Try again");
            }
        });
    }

    private static void reply(String host, String requestId, String token, String error) {
        try {
            JSONObject json = new JSONObject();
            json.put("requestId", requestId);
            json.put("idToken", token == null ? "" : token);
            json.put("error", error == null ? "" : error);
            UnityPlayer.UnitySendMessage(host, "OnNativeCredential", json.toString());
        } catch (Exception ignored) { /* No credentials in logs. Unity timeout resolves. */ }
    }
}
