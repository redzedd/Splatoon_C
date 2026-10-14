using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // Shader 編譯驗證:C# 的編譯檢查看不到 shader 錯誤,壞掉的 shader 只會在材質變洋紅時才被發現,
    // 而改 .hlsl 不會自動重編引用它的 .shader——所以這裡一律 ForceUpdate reimport 後再讀訊息表。
    public static class ShaderCompileCheck
    {
        private const string ShaderRoot = "Assets/Shaders";
        private const string MaterialRoot = "Assets/Materials";

        [MenuItem("Tools/SplatoonC/Shader Compile Check")]
        public static void Run()
        {
            string[] guids = AssetDatabase.FindAssets("t:Shader", new[] { ShaderRoot });
            var paths = new List<string>(guids.Length);
            foreach (string guid in guids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            paths.Sort();

            int errorCount = 0;
            int warningCount = 0;
            foreach (string path in paths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                {
                    Debug.LogError($"[SHADERCHECK] FAIL 無法載入 {path}");
                    errorCount++;
                    continue;
                }

                int messageCount = ShaderUtil.GetShaderMessageCount(shader);
                if (messageCount == 0)
                {
                    Debug.Log($"[SHADERCHECK] PASS {path}");
                    continue;
                }

                ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
                foreach (ShaderMessage message in messages)
                {
                    bool isError = message.severity == ShaderCompilerMessageSeverity.Error;
                    string line = $"[SHADERCHECK] {(isError ? "FAIL" : "WARN")} {path}({message.line}) {message.message} {message.messageDetails}";
                    if (isError)
                    {
                        errorCount++;
                        Debug.LogError(line);
                    }
                    else
                    {
                        warningCount++;
                        Debug.LogWarning(line);
                    }
                }
            }

            // 材質不會因為 shader 重匯入而更新:m_Floats 為空的材質是在「載入時」抓 shader 的預設值,
            // 只改 shader 預設值卻不重匯入材質,執行期看到的仍是舊數字(2026-09-10 實測 bit-identical 截圖)。
            int materialCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialRoot }))
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid),
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                materialCount++;
            }

            string verdict = errorCount == 0 ? "PASS" : "FAIL";
            Debug.Log($"[SHADERCHECK] DONE result={verdict} shaders={paths.Count} materials={materialCount} errors={errorCount} warnings={warningCount}");
        }
    }
}
