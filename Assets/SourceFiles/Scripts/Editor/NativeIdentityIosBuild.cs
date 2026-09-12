#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

internal static class NativeIdentityIosBuild
{
    [PostProcessBuild(100)]
    private static void Configure(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        string projectPath = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject();
        project.ReadFromFile(projectPath);
        string app = project.GetUnityMainTargetGuid();
        string framework = project.GetUnityFrameworkTargetGuid();
        project.AddFrameworkToProject(framework, "AuthenticationServices.framework", false);
        project.SetBuildProperty(framework, "CLANG_ENABLE_OBJC_ARC", "YES");
        string entitlements = project.GetBuildPropertyForAnyConfig(app, "CODE_SIGN_ENTITLEMENTS");
        if (string.IsNullOrEmpty(entitlements)) entitlements = "HazardHeights.entitlements";
        project.SetBuildProperty(app, "CODE_SIGN_ENTITLEMENTS", entitlements);
        project.WriteToFile(projectPath);

        var document = new PlistDocument();
        string fullPath = Path.Combine(path, entitlements);
        if (File.Exists(fullPath)) document.ReadFromFile(fullPath);
        var signIn = document.root.CreateArray("com.apple.developer.applesignin");
        signIn.AddString("Default");
        document.WriteToFile(fullPath);
    }
}
#endif
