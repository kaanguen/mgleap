using System.Linq;
using UnityEditor;
using UnityEngine;

// Mixamo uses its own skeleton definition. Unity retargets its Humanoid muscles
// to the Jogger Avatar at playback; copying the Jogger skeleton here is incorrect.
public sealed class JoggerWarmupImporter : AssetPostprocessor
{
    const string Folder = "Assets/JoggerWarmups/Mixamo/";
    static bool IsMixamo(string path) => path.StartsWith(Folder) && path.EndsWith(".fbx");
    void OnPreprocessModel()
    {
        if (IsMixamo(assetPath)) Configure((ModelImporter)assetImporter);
    }
    static void Configure(ModelImporter importer)
    {
        importer.importAnimation=true;
        importer.animationType=ModelImporterAnimationType.Human;
        importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar=null;
        importer.animationCompression=ModelImporterAnimationCompression.Off;
        importer.importCameras=false;
        importer.importLights=false;
        importer.materialImportMode=ModelImporterMaterialImportMode.None;
    }
    void OnPreprocessAnimation()
    {
        if(!IsMixamo(assetPath))return;
        var importer=(ModelImporter)assetImporter;
        var clips=importer.defaultClipAnimations;
        foreach(var clip in clips)
        {
            clip.name=System.IO.Path.GetFileNameWithoutExtension(assetPath);
            // These are complete source takes, including the start of Jumping Jacks.
            // Keep their timing, rather than forcing the endpoints into a fake loop.
            clip.loopTime=false;
            clip.loopPose=false;
            clip.lockRootRotation=true;
            clip.keepOriginalOrientation=true;
            clip.lockRootPositionXZ=true;
            clip.keepOriginalPositionXZ=true;
            clip.lockRootHeightY=true;
            bool plank=assetPath.EndsWith("/Start_Plank.fbx") || assetPath.EndsWith("/Plank.fbx") || assetPath.EndsWith("/End_Plank.fbx");
            // All three floor-exercise takes need a common authored height frame.
            // Feet-based root height changes reference while support moves to hands.
            clip.keepOriginalPositionY=plank;
            clip.heightFromFeet=!plank;
        }
        importer.clipAnimations=clips;
    }
    [MenuItem("Tools/Jogger/Configure Mixamo animation imports")]
    public static void ConfigureAll()
    {
        foreach(var name in new[]{"Batter_On_Deck","Start_Jumping_Jacks","Warming_Up","Arm_Stretching","Air_Squat","Start_Plank","Plank","End_Plank"})
        {
            var importer=AssetImporter.GetAtPath(Folder+name+".fbx") as ModelImporter;
            if(importer==null)continue;
            Configure(importer);importer.SaveAndReimport();
        }
    }
}
