namespace CQ_ChartMaker.Common;

/// <summary>
/// 应用版本信息与更新日志。
/// 发布新版本时只需更新本文件中的 Version 和 Changelog 两个常量。
/// </summary>
public static class AppInfo
{
    /// <summary> 当前版本号（显示在窗口标题栏） </summary>
    public const string Version = "v0.3.1(dev)";

    /// <summary> 窗口标题 = 程序名 + 版本号（const 之间可在编译期直接拼接） </summary>
    public const string WindowTitle = "CQ_ChartMaker " + Version;

    /// <summary> 更新日志（最新版本在最上面） </summary>
    public const string Changelog =
        """
        v0.3.1
        功能：
        -trackinfo新增曲绘画师、预览片段、预览BPM信息
        -!!!trackinfo序列化格式更改，旧版本的无法识别!!!
        -editor界面新增“跳转至有内容时间点”按钮
        -lanes界面改名为events界面
        -更改eventunit样式，新增eventunit显示数值曲线功能
        -新增拖拽dash/mute长度功能
        -修改带有锚点的tramsform的位置计算（理论上不属于制谱器功能更改，但对制谱器有影响，就加上了）
        -view界面新增“隐藏标号”功能
        bugfix：
        -修复transform串勾选问题
        -修复trackinfo修改乐曲偏移卡顿的问题（输入框失焦提交值重建音频，而不是每次更改都重建，但是重建音频本身还是会造成少量卡顿）
        -修复平铺模式下轨道重定位异常吸附
        
        v0.3.0
        ---7k定轨改为4+不定轨，并完成相关编辑界面---
        ---motion改为6种event，并更新其编辑界面---
        -吸附提示，快捷放置，快捷删除功能及其开关设置
        -主题色固定为#8E8CD8而不是跟随系统
        -inspector界面汉化设置
        -轨道透明度完全为0时editor界面对应轨道变暗处理
        
        v0.2.0
        ---修改项目结构，一个项目可以有多个谱面---
        ---新增TrackInfo界面及功能---
        -新增快捷键设置
        -新增长条放置偏好设置
        -新增节拍线抽稀防止BPM过高卡死
        -新增打击音效和音量设置
        -修复拖拽对齐bug，长条身体也能拖拽
        -crtl+a全选，双击轨道轨道全选
        -设置新增新建项目默认路径
        -新增打开项目文件夹功能
        -实装首拍偏移功能
        
        v0.1.5
        -新增版本号和内置更新日志
        
        v0.1.4
        -新增1/12，1/16细分线
        -输入框错误输入提示优化，失焦逻辑优化
        -view界面新增选中高亮，显示锚点
        -欢迎界面优化
        -可视化缓动曲线
        -notes/lanes/bpm的撤销重做池独立存储
        -crtl+c复制，crtl+x剪贴，crtl+s保存
        -新增拖拽功能
        -新增icon
        -新增设置界面

        v0.1.3
        -为motion添加初始值
        -添加step曲线
        """;
}
