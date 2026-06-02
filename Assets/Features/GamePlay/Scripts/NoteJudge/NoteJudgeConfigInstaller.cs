using UnityEngine;
using Zenject;

[CreateAssetMenu(fileName = "NoteJudgeConfigInstaller", menuName = "Installers/NoteJudgeConfigInstaller")]
public class NoteJudgeConfigInstaller : ScriptableObjectInstaller<NoteJudgeConfigInstaller>
{
    // 这个字段会在Inspector面板中显示，方便你拖拽或编辑配置数据
    public NoteJudgeConfig noteJudgeConfig;
    
    public override void InstallBindings()
    {
        // 将 noteJudgeConfig 这个实例绑定到容器中
        Container.BindInstance(noteJudgeConfig);
    }
}