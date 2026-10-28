using UnityEditor;
using System;
using System.IO;
[InitializeOnLoad]
internal static class CodexOxygenIntegrationOnce {
 static CodexOxygenIntegrationOnce() {
  if (SessionState.GetBool("CodexOxygenIntegrationOnce.v2",false)) return;
  EditorApplication.delayCall += () => {
   SessionState.SetBool("CodexOxygenIntegrationOnce.v2",true);
   try { OxygenBubblesSetup.InstallAndVerify(); }
   catch(Exception e) {
    Directory.CreateDirectory("Temp/OxygenBubbleIntegration");
    File.WriteAllText("Temp/OxygenBubbleIntegration/error.txt",e.ToString());
    UnityEngine.Debug.LogException(e);
   }
  };
 }
}
