using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CriWare;
using System.IO;

public class CriLoader : MonoBehaviour
{
    
    #region Mono饿汉单例代码

    private static CriLoader _instance;
    public static CriLoader Instance => _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeRuntime()
    {
        GameObject obj = new GameObject("CriLoder");
        _instance = obj.AddComponent<CriLoader>();
        DontDestroyOnLoad(obj);
    }

    #endregion
    
    private CriWareInitializer initializer;
    private CriAtom atom;
    private CriWareErrorHandler errorHandler;
    
    /// <summary>
    /// 初始化：挂载必要组件
    /// </summary>
    public void Initialize()
    {
        initializer = gameObject.AddComponent<CriWareInitializer>();
        atom = gameObject.AddComponent<CriAtom>();
        errorHandler = gameObject.AddComponent<CriWareErrorHandler>();
    }
    
    /// <summary>
    /// 手动注册 ACF 文件（路径相对于 StreamingAssets）
    /// </summary>
    public void RegisterAcf(string acfFileName)
    {
        string acfPath = Path.Combine(Application.streamingAssetsPath, acfFileName);
        CriAtomEx.RegisterAcf(null, acfPath);
        atom.acfFile = acfFileName;
        Debug.Log($"[CriLoader] ACF 已注册：{acfPath}");
    }
    
    /// <summary>
    /// 手动添加 Cue Sheet（支持名称、ACB 路径，可选 AWB 路径）
    /// </summary>
    public void AddCueSheet(string cueSheetName, string acbFileName, string awbFileName = null)
    {
        string acbPath = Path.Combine(Application.streamingAssetsPath, acbFileName);
        string awbPath = string.IsNullOrEmpty(awbFileName) ? null : Path.Combine(Application.streamingAssetsPath, awbFileName);
        CriAtom.AddCueSheet(cueSheetName, acbPath, awbPath, null);
        Debug.Log($"[CriLoader] Cue Sheet 已添加：{cueSheetName}，ACB={acbPath}，AWB={awbPath ?? "无"}");
    }
    
    /// <summary>
    /// 手动移除 Cue Sheet（按名称）
    /// </summary>
    public void RemoveCueSheet(string cueSheetName)
    {
        CriAtom.RemoveCueSheet(cueSheetName);
        Debug.Log($"[CriLoader] Cue Sheet 已移除：{cueSheetName}");
    }
    
    /// <summary>
    /// 自动注册 StreamingAssets 中找到的第一个 .acf 文件。
    /// 如果找到多个，只注册第一个。
    /// </summary>
    public void AutoRegisterAcf()
    {
        string searchPath = Application.streamingAssetsPath;
        if (!Directory.Exists(searchPath))
        {
            Debug.LogWarning($"[CriLoader] StreamingAssets 目录不存在: {searchPath}");
            return;
        }

        string[] acfFiles = Directory.GetFiles(searchPath, "*.acf", SearchOption.TopDirectoryOnly);
        if (acfFiles.Length == 0)
        {
            Debug.LogWarning("[CriLoader] 未找到任何 .acf 文件");
            return;
        }

        // 取第一个文件
        string acfPath = acfFiles[0];
        CriAtomEx.RegisterAcf(null, acfPath);
        Debug.Log($"[CriLoader] 已自动注册 ACF 文件: {acfPath}");
    }
    
    /// <summary>
    /// 自动注册 StreamingAssets 中找到的所有 .acb 文件作为 Cue Sheet。
    /// Cue Sheet 名称使用 ACB 文件名（不含扩展名）。
    /// 如果存在同名的 .awb 文件，则一并加载。
    /// </summary>
    public void AutoAddCueSheets()
    {
        string searchPath = Application.streamingAssetsPath;
        if (!Directory.Exists(searchPath))
        {
            Debug.LogWarning($"[CriLoader] StreamingAssets 目录不存在: {searchPath}");
            return;
        }

        string[] acbFiles = Directory.GetFiles(searchPath, "*.acb", SearchOption.TopDirectoryOnly);
        if (acbFiles.Length == 0)
        {
            Debug.LogWarning("[CriLoader] 未找到任何 .acb 文件");
            return;
        }

        foreach (string acbPath in acbFiles)
        {
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(acbPath);
            string directory = Path.GetDirectoryName(acbPath);
            string awbPath = Path.Combine(directory, fileNameWithoutExt + ".awb");
            
            // 检查 AWB 文件是否存在
            if (!File.Exists(awbPath))
                awbPath = null;

            // 添加 Cue Sheet
            CriAtom.AddCueSheet(fileNameWithoutExt, acbPath, awbPath, null);
            Debug.Log($"[CriLoader] 已添加 Cue Sheet: {fileNameWithoutExt}, ACB={acbPath}, AWB={(awbPath ?? "无")}");
        }
    }
}
