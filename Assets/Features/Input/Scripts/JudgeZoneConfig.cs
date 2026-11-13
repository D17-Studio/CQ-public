using System;
using UnityEngine;

/// <summary> 判定区原始配置：L1/L2/R1/R2 单键 + 三组额外键；Left/Right/All 由 JudgeZoneCatalog 派生。 </summary>
[Serializable]
public struct JudgeZoneConfigData
{
    public JudgeKeyAction L1;
    public JudgeKeyAction L2;
    public JudgeKeyAction R1;
    public JudgeKeyAction R2;

    public JudgeKeyAction[] ExtraLeft;
    public JudgeKeyAction[] ExtraRight;
    public JudgeKeyAction[] ExtraAll;
}

/// <summary>
/// 判定区唯一配置源：由用户创建并配置资产。
/// </summary>
[CreateAssetMenu(fileName = "JudgeZoneConfig", menuName = "Input/JudgeZoneConfig")]
public class JudgeZoneConfig : ScriptableObject
{
    [SerializeField] private JudgeZoneConfigData _zones;

    public JudgeZoneConfigData Zones => _zones;
}
