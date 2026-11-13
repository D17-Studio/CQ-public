/// <summary>
/// 将按键名转换为显示用简短形式
/// </summary>
public static class KeyNameDisplay
{
    public static string Format(string bindingName)
    {
        if (bindingName == "Space")
            return "_";

        bindingName = bindingName
            .Replace("Alpha", "")
            .Replace("Keypad", "K");

        if (bindingName.Length > 3)
            return bindingName[..3];

        return bindingName;
    }
}
