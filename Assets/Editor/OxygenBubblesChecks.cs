using System;
using System.IO;
using System.Reflection;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class OxygenBubblesChecks
{
    internal static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    internal static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    internal static void Check(bool condition, string description)
    { if (!condition) throw new InvalidOperationException("[OxygenBubbles CHECK] FAIL: " + description); }

    [MenuItem("Tools/Treadmill/Checks/Check blue oxygen bubbles")]
    public static void Verify()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        TrackCollectibles pickups = null;
        try
        {
            var host = new GameObject("Oxygen integration fixture");
            SceneManager.MoveGameObjectToScene(host, scene);
            var head = new GameObject("Head").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(head.gameObject, scene);
            head.transform.position = new Vector3(0,1.7f,0);
            var world = host.AddComponent<StraightTreadmillWorld>();
            Set(world, "headCamera", head);
            Set(world, "requireMarkerPlacement", false);
            Set(world, "requireHeightCalibration", false);
            world.ResetDistance();
            world.ConfigureUserSpeed(RunnerSpeedSource.FixedTest,36);
            pickups = host.AddComponent<TrackCollectibles>();
            Set(pickups,"world",world);
            Set(pickups,"collectiblePrefab",AssetDatabase.LoadAssetAtPath<GameObject>(OxygenBubblesSetup.PrefabPath));
            pickups.Tick(0);
            var container = (Transform)Get(pickups,"container");
            var first = container.GetChild(0).GetComponent<TrackOxygenBubbleVisual>();
            Check(first != null,"new visual is used by the existing spawner");
            Check(Vector3.Dot(first.transform.forward,(head.transform.position-first.transform.position).normalized) > .999f,"O2 faces the viewer");
            Check(!first.FlightTrail.emitting,"no trail while floating outside magnet range");
            world.AdvanceSimulation(.4f); pickups.Tick(.4f);
            Check(first.FlightTrail.emitting,"trail starts during the magnet approach");
            var position = first.transform.position;
            var model = (Transform)Get(first,"model");
            var scale = model.localScale;
            world.SessionPaused = true; pickups.Tick(1);
            Check(first.transform.position==position && model.localScale==scale && !first.FlightTrail.emitting,"pause freezes pulse and approach");
            world.SessionPaused = false;
            int events=0; pickups.Collected += value => events++;
            world.AdvanceSimulation(.2f); pickups.Tick(.2f);
            Check(pickups.CollectedCount==1 && events==1,"existing count and event fire once at the player");
            Check(!first.gameObject.activeSelf && !first.FlightTrail.emitting && first.FlightTrail.positionCount==0,"pooled bubble clears its old trail");
            pickups.Tick(0);
            Check(pickups.CollectedCount==1 && events==1,"collection cannot fire twice");
            foreach (var renderer in AssetDatabase.LoadAssetAtPath<GameObject>(OxygenBubblesSetup.PrefabPath).GetComponentsInChildren<Renderer>())
            foreach (var material in renderer.sharedMaterials)
                Check(material!=null && !ShaderUtil.ShaderHasError(material.shader),"URP shader compiles");
            Debug.Log("[OxygenBubbles CHECK] PASS: existing spawner, facing, magnet trail, pulse pause, one collection event, pooled trail reset and URP shaders.");
        }
        finally
        {
            if (pickups!=null) typeof(TrackCollectibles).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pickups,null);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    public static void RenderPreview()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture texture = null;
        Texture2D pixels = null;
        var previous = RenderTexture.active;
        try
        {
            var sample = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(OxygenBubblesSetup.PrefabPath));
            SceneManager.MoveGameObjectToScene(sample,scene);
            sample.transform.localScale = Vector3.one;
            var head = new GameObject("Oxygen preview camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(head.gameObject,scene);
            head.scene=scene;
            head.transform.position = new Vector3(0,.12f,-1.5f);
            head.transform.LookAt(Vector3.zero);
            head.nearClipPlane=.02f; head.farClipPlane=10;
            head.clearFlags=CameraClearFlags.SolidColor;
            head.backgroundColor=new Color(.008f,.015f,.04f);
            sample.GetComponent<TrackOxygenBubbleVisual>().Animate(head,.3f,false,false);
            texture = new RenderTexture(1000,800,24) { antiAliasing=4 };
            head.targetTexture=texture; head.Render();
            RenderTexture.active=texture;
            pixels=new Texture2D(1000,800,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,1000,800),0,0); pixels.Apply();
            Directory.CreateDirectory("Temp/OxygenBubbleIntegration");
            File.WriteAllBytes("Temp/OxygenBubbleIntegration/preview.png",pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=previous;
            if (texture!=null) UnityEngine.Object.DestroyImmediate(texture);
            if (pixels!=null) UnityEngine.Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
