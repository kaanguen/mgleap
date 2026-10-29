using System.IO;
using StadionTracking;
using TMPro;
using UnityEditor;
using UnityEngine;

internal static class RunnerWarmupRenderChecks
{
    private static void VerifyStereoBounds(Camera camera)
    {
        // Intentionally asymmetric eye projections: a mono viewport must not determine XR HUD bounds.
        camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, Matrix4x4.Frustum(-.035f, .045f, -.03f, .025f, .05f, 100));
        camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, Matrix4x4.Frustum(-.045f, .035f, -.025f, .03f, .05f, 100));
        var leftView = camera.worldToCameraMatrix * Matrix4x4.Translate(new Vector3(.032f, 0, 0));
        var rightView = camera.worldToCameraMatrix * Matrix4x4.Translate(new Vector3(-.032f, 0, 0));
        camera.SetStereoViewMatrix(Camera.StereoscopicEye.Left, leftView);
        camera.SetStereoViewMatrix(Camera.StereoscopicEye.Right, rightView);
        try
        {
            const float distance = 1.8f;
            var common = RunnerWarmupHud.VisibleRect(camera, distance, true);
            if (common.width <= 0 || common.height <= 0) throw new System.InvalidOperationException("Stereo HUD has no common eye bounds.");
            float scale = Mathf.Min(common.width * .7f / 560, common.height * .16f / 112);
            var centre = new Vector3(common.center.x, Mathf.Lerp(common.yMin, common.yMax, .78f), distance);
            foreach (var eye in new[] { Camera.MonoOrStereoscopicEye.Left, Camera.MonoOrStereoscopicEye.Right })
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
            {
                var corner = camera.transform.TransformPoint(centre + new Vector3(x * 280 * scale, y * 56 * scale, 0));
                var view = camera.WorldToViewportPoint(corner, eye);
                if (view.x < .01f || view.x > .99f || view.y < .01f || view.y > .99f || view.z <= camera.nearClipPlane)
                    throw new System.InvalidOperationException("HUD outside asymmetric eye view: " + eye + " " + view);
            }
            Debug.Log("[Warmup Stereo] PASS: full banner in both asymmetric eye frusta.");
        }
        finally { camera.ResetStereoProjectionMatrices(); camera.ResetStereoViewMatrices(); }
    }

    [MenuItem("Tools/Jogger/Checks/Capture Warm-up HUD")]
    public static void Capture()
    {
        RunnerWarmupHudAssets.Ensure();
        using (var f = new RunnerAvatarBehaviourChecks.Fixture())
        {
            var camera = f.world.HeadCamera;
            camera.scene = f.scene;
            camera.transform.rotation = Quaternion.identity;
            camera.fieldOfView = 45; camera.aspect = 1.28f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 5;
            // An opaque object in front must not hide a HUD instruction.
            var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(occluder, f.scene);
            occluder.layer = 5; occluder.transform.position = camera.transform.position + camera.transform.forward;
            occluder.transform.localScale = new Vector3(8, 8, .05f);
            var black = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            black.SetColor("_BaseColor", Color.black);
            occluder.GetComponent<Renderer>().sharedMaterial = black;
            var warmup = RunnerWarmup.GetOrCreate(f.world);
            warmup.Begin(new[] { new RunnerWarmupStep("Mixamo_ArmStretching", 30) });
            for (int frame = 0; frame < 1000 && warmup.Approaching; frame++)
            {
                f.companion.Synchronize(); warmup.Tick(.05f); f.companion.Animator.Update(.05f);
            }
            var hud = RunnerWarmupHud.GetOrCreate(f.world);
            var rt = new RenderTexture(1280, 1000, 24);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(1280, 1000, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt;
                VerifyStereoBounds(camera);
                RunnerAvatarBehaviourChecks.Call(hud, "LateUpdate");
                Canvas.ForceUpdateCanvases();
                foreach (var text in hud.Banner.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
                var title = hud.Banner.GetComponentsInChildren<TMP_Text>()[0];
                if (!title.fontSharedMaterial.shader.name.Contains("Overlay")) throw new System.InvalidOperationException("Warm-up font must use an included overlay shader.");
                // Late XR poses must also reposition the banner before rendering.
                camera.transform.position += new Vector3(.3f, .1f, -.2f);
                camera.transform.rotation = Quaternion.Euler(4, 30, 0);
                occluder.transform.SetPositionAndRotation(camera.transform.position + camera.transform.forward, camera.transform.rotation);
                RunnerAvatarBehaviourChecks.Call(hud, "BeforeRender");
                var viewport = camera.WorldToViewportPoint(hud.Banner.transform.position);
                if (Mathf.Abs(viewport.x - .5f) > .001f || Mathf.Abs(viewport.y - .78f) > .001f) throw new System.InvalidOperationException("Warm-up HUD missed the latest XR head pose.");
                camera.Render(); RenderTexture.active = rt;
                pixels.ReadPixels(new Rect(0, 0, 1280, 1000), 0, 0); pixels.Apply();
                Directory.CreateDirectory("Library/WarmupVerification");
                File.WriteAllBytes("Library/WarmupVerification/hud.png", pixels.EncodeToPNG());
                int bright = 0;
                foreach (var pixel in pixels.GetPixels32()) if (pixel.r > 50 || pixel.g > 50 || pixel.b > 50) bright++;
                if (bright < 1500) throw new System.InvalidOperationException("Warm-up HUD failed the actual pixel render over an occluder: " + bright);
                Debug.Log("[Warmup Render] PASS: visible title/countdown over depth occluder, late pose follow, included overlay material; bright pixels=" + bright);
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(pixels); Object.DestroyImmediate(black);
            }
        }
    }
}
