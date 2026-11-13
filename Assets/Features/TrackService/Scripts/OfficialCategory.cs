/// <summary>
/// 官谱分类 id：仅作分组标识与后续章节展示匹配，不参与排序；分类顺序由 OfficialTrackCategoryConfig 的条目顺序决定。
/// 新增分类只能在末尾追加——config 资产按整数序列化枚举值，中间插入会导致已有配置错位。
/// </summary>
public enum OfficialCategory
{
    // 漏选哨兵，不参与分组
    None,
    
    // 测试曲目
    Test,

    // 旧谱
    Legacy,

    // 单曲
    Single,

    // 序章
    Prologue,

    Chapter1,
    Chapter2,
}
