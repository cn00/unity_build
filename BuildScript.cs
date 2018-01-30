#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Diagnostics;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

public class UPath
{
    public static string go(string path)
    {
        return path.Trim()
            .TrimStart()
            .TrimEnd()
            .Replace("\\", "/")
            .Replace("//", "/")
            ;
    }
}

[Serializable]
public class FGVersion
{
    public int Major = 0; // 主版本
    public int Minor = 0; // 次版本
    public int Patch = 0; // 补丁版本

    public FGVersion(string v)
    {
        var vs = v.Split('.');
        Major = int.Parse(vs[0]);
        Minor = int.Parse(vs[1]);
        if(vs.Length == 3)
            Patch = int.Parse(vs[2]);
    }
    public FGVersion(int major, int minor, int build)
    {
        Major = major;
        Minor = minor;
        Patch = build;
    }

    public override string ToString()
    {
        return string.Format("{0}.{1}.{2}", Major, Minor, Patch);
    }

    public Version V
    {
        get { return new Version(Major, Minor, Patch); }
    }

    public static implicit operator FGVersion(Version v)
    {
        return new FGVersion(v.Major, v.Minor, v.Build);
    }

}

[ExecuteInEditMode]
public class BuildScript
{
    #region Common

    static string[] SCENES = FindEnabledEditorScenes();
    static string APP_NAME = "game";
    static string DATETIME = DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss");
    static string TARGET_DIR = "bin/";

    const string BundleWorkingSpace = "AssetBundle/";

    private static string[] FindEnabledEditorScenes()
    {
        List<string> EditorScenes = new List<string>();
        foreach(EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if(!scene.enabled)
                continue;
            EditorScenes.Add(scene.path);
        }
        return EditorScenes.ToArray();
    }

    static void GenericBuild(string[] scenes, string target_dir, BuildTargetGroup targetGroup, BuildTarget build_target, BuildOptions build_options)
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, build_target);
        string res = BuildPipeline.BuildPlayer(scenes, target_dir, build_target, build_options);
        if(res.Length > 0)
        {
            UnityEngine.Debug.LogFormat("<color=red>BuildPlayer error: {0}</color>", res);
        }

        ProcessStartInfo pi = new ProcessStartInfo(
#if UNITY_EDITOR_WIN
            "explorer.exe",
#elif UNITY_EDITOR_OSX
            "open",
#endif
            TARGET_DIR
        );
        Process.Start(pi);
    }

    public static List<string> ExcludeExtensions = new List<string>()
    {
        ".tmp",
        ".bak",
        ".unity",
        ".meta",
        ".lua",
    };

    static string TargetName(BuildTarget target)
    {
        switch(target)
        {
        case BuildTarget.Android:
            return "Android";
        case BuildTarget.iOS:
            return "iOS";
        case BuildTarget.StandaloneWindows:
        case BuildTarget.StandaloneWindows64:
            return "Windows";
        case BuildTarget.StandaloneOSXIntel:
        case BuildTarget.StandaloneOSXIntel64:
        case BuildTarget.StandaloneOSXUniversal:
            return "OSX";
        default:
            return null;
        }
    }

    #region AssetBundle
    public static void BuildBundle(string indir, string outdir, BuildTarget targetPlatfrom)
    {
        try
        {
            outdir = BundleWorkingSpace + TargetName(BuildTarget.Android) + "/" + PlayerSettings.bundleVersion + "/" + outdir;
            if(!Directory.Exists(outdir))
            {
                Directory.CreateDirectory(outdir);
            }

            // lua
            foreach(var f in Directory.GetFiles(indir, "*.lua*", SearchOption.AllDirectories))
            {
                File.Copy(f, f.Replace(".lua", ".lua.txt"));
            }
            AssetDatabase.Refresh();

            // Create the array of bundle build details.
            var buildMap = new List<AssetBundleBuild>();
            float count = 0;
            var dirs = Directory.GetDirectories(indir, "*", SearchOption.TopDirectoryOnly);
            foreach(var dir in dirs)
            {

                ++count;
                var udir = UPath.go(dir);
                var assetBundleName = dir.Substring(udir.LastIndexOf("/") + 1);
                UnityEngine.Debug.Log("pack: " + assetBundleName);
                var ab = CreateAssetBundleBuild(udir, assetBundleName, ExcludeExtensions);
                if(ab != null)
                    buildMap.Add(ab.Value);
                EditorUtility.DisplayCancelableProgressBar("BuildBundle ...", udir, count / dirs.Length);
                BuildPipeline.BuildAssetBundles(
                    outdir,
                    buildMap.ToArray(),
                    (
                        BuildAssetBundleOptions.UncompressedAssetBundle
                    //| BuildAssetBundleOptions.AppendHashToAssetBundleName
                    ),
                    targetPlatfrom);
            }

            foreach(var f in Directory.GetFiles(indir, "*.lua.txt*", SearchOption.AllDirectories))
            {
                File.Delete(f);
            }
            AssetDatabase.Refresh();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static AssetBundleBuild? CreateAssetBundleBuild(string assetDir, string assetBundleName, List<string> excludes)
    {
        var ab = new AssetBundleBuild();
        ab.assetBundleName = assetBundleName + ".fg";

        // 如果上次打包以来为更新过此 bundle 未编辑过则跳过
        long newWriteTime = 0;
        long lastWriteTime = 0;
        var flastWriteTime = assetDir + @"/.lastWriteTime";
        if(File.Exists(flastWriteTime))
        {
            StreamReader reader = new StreamReader(flastWriteTime);
            if(reader != null)
            {
                string s = reader.ReadLine();
                reader.Close();
                if(!long.TryParse(s, out lastWriteTime))
                {
                    lastWriteTime = 0;
                }
                newWriteTime = lastWriteTime;
            }
        }

        var assetNames = new List<string>();
        int nnew = 1; // =0
        foreach(var f in Directory.GetFiles(assetDir, "*.*", SearchOption.AllDirectories))
        {
            if(excludes.Contains(Path.GetExtension(f))
                || Path.GetFileName(f) == ".lastWriteTime")
                continue;
            assetNames.Add(f);

            var finfo = new FileInfo(f);
            if(finfo.LastWriteTime.ToFileTimeUtc() > newWriteTime)
            {
                ++nnew;
                newWriteTime = finfo.LastWriteTime.ToFileTimeUtc();
                UnityEngine.Debug.Log(UPath.go(f) + ": " + DateTime.FromFileTimeUtc(newWriteTime));
            }
        }
        ab.assetNames = assetNames.ToArray();

        if(nnew > 0)
        {
            StreamWriter writer = new StreamWriter(flastWriteTime);
            writer.Write(newWriteTime);
            writer.Close();
            UnityEngine.Debug.Log(assetDir + "> " + DateTime.FromFileTimeUtc(newWriteTime));

            return ab;
        }
        else
        {
            return null;
        }
    }

    #endregion AssetBundle

    public static void StreamingSceneBuild(string scene, string path, BuildTarget targetPlatform)
    {
        StreamingSceneBuild(new[] { scene }, path, targetPlatform);
    }
    public static void StreamingSceneBuild(string[] scenes, string outName, BuildTarget targetPlatform)
    {
        string SceneOutPath = BundleWorkingSpace + TargetName(targetPlatform) + "/" + PlayerSettings.bundleVersion + "/Level/" + outName + ".fg";

        var dir = Path.GetDirectoryName(SceneOutPath);
        if(!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        BuildPipeline.BuildPlayer(
            scenes,
            SceneOutPath,
            targetPlatform,
            BuildOptions.BuildAdditionalStreamedScenes);
    }

    #endregion Common

    #region 安卓安装包
    [MenuItem("Build/Android_APK")]
    static void BuildAndroidApk()
    {
        AssetBundleBuildLocalStreamingAsset.Build();

        var version = new FGVersion(PlayerSettings.bundleVersion);
        // TODO: open this when release
        // version.Minor += 1;
        version.Patch = 0;
        PlayerSettings.bundleVersion = version.ToString();
        PlayerSettings.Android.bundleVersionCode += 1;
        var versionCode = PlayerSettings.Android.bundleVersionCode;

        string target_dir = TARGET_DIR + APP_NAME + ".apk";
        GenericBuild(SCENES, target_dir, BuildTargetGroup.Android, BuildTarget.Android, BuildOptions.None);
    }

    #endregion 安卓打包

    #region 🍎安装包

    [MenuItem("Build/iOS (iL2cpp proj)")]
    static void ExportIOSProj()
    {
        AssetBundleBuildLocalStreamingAsset.Build();

        var version = new FGVersion(PlayerSettings.bundleVersion);
        //version.Minor += 1;
        version.Patch = 0;
        PlayerSettings.bundleVersion = version.ToString();
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        // add manualy or outer script
        // PlayerSettings.iOS.buildNumber = (int.Parse(PlayerSettings.iOS.buildNumber) + 1).ToString();

        string target_dir = Environment.GetEnvironmentVariable("IosProjDir");
        if (string.IsNullOrEmpty(target_dir))
        {
            target_dir = "ios.proj";
        }
        var option = BuildOptions.EnableHeadlessMode | BuildOptions.SymlinkLibraries | BuildOptions.Il2CPP;
        version.Patch = 0;
        if(Environment.GetEnvironmentVariable("configuration") == "Release")
        {
            PlayerSettings.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
        }
        else
        {
            PlayerSettings.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
            option |= BuildOptions.AllowDebugging;
        }
        GenericBuild(SCENES, target_dir, BuildTargetGroup.iOS, BuildTarget.iOS, option);
    }

    [MenuItem("Build/iOS Sim (iL2cpp proj)")]
    static void ExportIOSProjSim()
    {
        AssetBundleBuildLocalStreamingAsset.Build();

        var version = new FGVersion(PlayerSettings.bundleVersion);
        //version.Minor += 1;
        PlayerSettings.bundleVersion = version.ToString();
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
//        PlayerSettings.iOS.buildNumber = (int.Parse(PlayerSettings.iOS.buildNumber) + 1).ToString();
        var versionCode = int.Parse(PlayerSettings.iOS.buildNumber);

        string target_dir = "ios.sim.proj";
        var option = BuildOptions.EnableHeadlessMode | BuildOptions.SymlinkLibraries | BuildOptions.Il2CPP;
        version.Patch = 0;
        if(Environment.GetEnvironmentVariable("configuration") == "Release")
        {
            PlayerSettings.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
        }
        else
        {
            PlayerSettings.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
            option |= BuildOptions.AllowDebugging;
        }
        GenericBuild(SCENES, target_dir, BuildTargetGroup.iOS, BuildTarget.iOS, option);
    }

    [MenuItem("Build/Mac OS X")]
    static void MacOSX()
    {
        string target_dir = TARGET_DIR + "/" + APP_NAME + "-" + DATETIME + ".app";
        GenericBuild(SCENES, target_dir, BuildTargetGroup.Standalone, BuildTarget.StandaloneOSXIntel, BuildOptions.None);
    }
    #endregion 🍎打包

    #region Windows

    [MenuItem("Build/Windows")]
    static void BuildWindows()
    {
        string target_dir = TARGET_DIR + "/" + DATETIME + "/" + APP_NAME + ".exe";
        GenericBuild(SCENES, target_dir, BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows, BuildOptions.None);
    }

    #endregion Windows
}
#endif