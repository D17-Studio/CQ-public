using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputSystem : MonoSingletonHungry<InputSystem>
{
    public bool  Esc => _inputActions.UIActions.Back.IsPressed();
    public bool EscDown => _inputActions.UIActions.Back.WasPressedThisFrame();
    public bool EscUp => _inputActions.UIActions.Back.WasReleasedThisFrame();
    
    
    public bool LaneHold(int index)
    {
        return index is >= -3 and <= 3 && lanesAction[index].IsPressed();
    }
    public bool LaneDown(int index)
    {
        return index is >= -3 and <= 3 && lanesAction[index].WasPressedThisFrame();
    }
    public bool LaneUp(int index)
    {
        return index is >= -3 and <= 3 && lanesAction[index].WasReleasedThisFrame();
    }
    
    
    private InputActions _inputActions;

    private Dictionary<int, InputAction> lanesAction =  new Dictionary<int, InputAction>();
    
    private void Awake()
    {
        // 实例化
        _inputActions = new InputActions();

        // 创建字典
        lanesAction = new Dictionary<int, InputAction>
        {
            { -3, _inputActions.GameActions.Lanel3 },
            { -2, _inputActions.GameActions.Lanel2 },
            { -1, _inputActions.GameActions.Lanel1 },
            { 0, _inputActions.GameActions.Lane0 },
            { 1, _inputActions.GameActions.Laner1 },
            { 2, _inputActions.GameActions.Laner2 },
            { 3, _inputActions.GameActions.Laner3 },
        };
        
        // 加载配置文件
        LoadBindingsFromFile();
    }
    
    private void OnEnable()
    {
        // 启用输入
        _inputActions.Enable();
    }

    private void OnDisable()
    {
        // 禁用输入
        _inputActions.Disable();
    }
    

   


    /// <summary>
    /// 重新绑定轨道按键
    /// </summary>
    /// <param name="index"></param>
    /// <param name="onComplete"></param>
    /// <param name="onCancel"></param>
    public void RebindTrackAction(int index, Action onComplete = null , Action onCancel = null)
    {
        if (index is >= -3 and <= 3)
        {
            if (onCancel != null) onCancel.Invoke();
            return;
        }
        var trackAction = lanesAction[index];
        
        trackAction.Disable();
        
        trackAction.PerformInteractiveRebinding()
            .WithCancelingThrough("<Keyboard>/escape")
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithControlsExcluding("Mouse") 
            .OnMatchWaitForAnother(0.1f)
            .OnComplete(data => 
            {
                // 监听成功，新绑定已自动应用！
                Debug.Log($"重新绑定完成，新路径: {data.action.bindings[0].effectivePath}");
                data.Dispose(); // 清理资源
                trackAction.Enable(); // 重新启用
                if (onComplete != null) onComplete.Invoke();//回调
                SaveBindingsToFile();
            })
            .OnCancel(data => 
            {
                // 玩家取消了监听
                Debug.Log("重新绑定取消");
                data.Dispose();
                trackAction.Enable();
                if (onCancel != null) onCancel.Invoke();//回调
                
            })
            .Start(); // 启动监听
        
        trackAction.Enable();
    }


    /// <summary>
    /// 获取轨道绑定的按键名
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public string GetLineBindingName(int index)
    {
        if (index is >= -3 and <= 3)
        {
            return GetBindingDisplayName(lanesAction[index], 0);
        }
        return "";
    }
    
    
    //获取绑定按键的名字
    private string GetBindingDisplayName(InputAction action, int bindingIndex = 0)
    {
        // 获取有效的绑定路径（考虑覆盖绑定）
        string effectivePath = action.bindings[bindingIndex].effectivePath;
    
        // 转换为人类可读的字符串，并且省略设备名称（只显示按键本身）
        string displayName = InputControlPath.ToHumanReadableString(
            effectivePath,
            InputControlPath.HumanReadableStringOptions.OmitDevice
        );
        return displayName;
    }
     
    
    /// <summary>
    /// 保存绑定设置
    /// </summary>
    private void SaveBindingsToFile()
    {
        string json = _inputActions.asset.SaveBindingOverridesAsJson();
        string path = Path.Combine(Application.persistentDataPath, "customBindings.json");
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// 加载绑定设置
    /// </summary>
    private void LoadBindingsFromFile()
    {
        string path = Path.Combine(Application.persistentDataPath, "customBindings.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            _inputActions.asset.LoadBindingOverridesFromJson(json);
        }
    }
}
