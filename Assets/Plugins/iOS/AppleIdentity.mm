#import <AuthenticationServices/AuthenticationServices.h>
#import <UIKit/UIKit.h>

extern "C" void UnitySendMessage(const char *, const char *, const char *);
extern "C" UIViewController *UnityGetGLViewController(void);

@interface HHAppleIdentity : NSObject <ASAuthorizationControllerDelegate,
                                       ASAuthorizationControllerPresentationContextProviding>
@property(nonatomic, copy) NSString *host;
@property(nonatomic, copy) NSString *requestId;
@property(nonatomic, strong) ASAuthorizationController *controller;
@end

static HHAppleIdentity *pendingAppleIdentity;

@implementation HHAppleIdentity
- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController *)controller {
    return UnityGetGLViewController().view.window;
}
- (void)finish:(NSString *)token error:(NSString *)error {
    if (pendingAppleIdentity != self) return;
    NSDictionary *payload = @{ @"requestId": self.requestId, @"idToken": token ?: @"",
                               @"error": error ?: @"" };
    NSData *data = [NSJSONSerialization dataWithJSONObject:payload options:0 error:nil];
    NSString *json = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    if (json) UnitySendMessage(self.host.UTF8String, "OnNativeCredential", json.UTF8String);
    self.controller = nil;
    pendingAppleIdentity = nil;
}
- (void)authorizationController:(ASAuthorizationController *)controller
   didCompleteWithAuthorization:(ASAuthorization *)authorization {
    if (![authorization.credential isKindOfClass:[ASAuthorizationAppleIDCredential class]]) {
        [self finish:nil error:@"Unexpected Apple credential"];
        return;
    }
    ASAuthorizationAppleIDCredential *credential = (ASAuthorizationAppleIDCredential *)authorization.credential;
    if (![credential.state isEqualToString:self.requestId]) {
        [self finish:nil error:@"Apple sign-in response expired. Try again"];
        return;
    }
    NSString *token = [[NSString alloc] initWithData:credential.identityToken encoding:NSUTF8StringEncoding];
    [self finish:token error:token.length ? nil : @"No Apple sign-in credential received"];
}
- (void)authorizationController:(ASAuthorizationController *)controller didCompleteWithError:(NSError *)error {
    BOOL cancelled = [error.domain isEqualToString:ASAuthorizationErrorDomain] &&
                     error.code == ASAuthorizationErrorCanceled;
    [self finish:nil error:cancelled ? @"Sign-in cancelled" : @"Apple sign-in failed. Try again"];
}
@end

extern "C" void HHAppleSignIn(const char *host, const char *requestId, const char *nonceHash) {
    NSString *hostValue = [NSString stringWithUTF8String:host];
    NSString *requestValue = [NSString stringWithUTF8String:requestId];
    NSString *nonceValue = [NSString stringWithUTF8String:nonceHash];
    dispatch_async(dispatch_get_main_queue(), ^{
        HHAppleIdentity *delegate = [HHAppleIdentity new];
        delegate.host = hostValue;
        delegate.requestId = requestValue;
        pendingAppleIdentity = delegate;
        ASAuthorizationAppleIDRequest *request = [[ASAuthorizationAppleIDProvider new] createRequest];
        request.requestedScopes = @[ASAuthorizationScopeEmail];
        request.nonce = nonceValue;
        request.state = requestValue;
        delegate.controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[request]];
        delegate.controller.delegate = delegate;
        delegate.controller.presentationContextProvider = delegate;
        [delegate.controller performRequests];
    });
}
