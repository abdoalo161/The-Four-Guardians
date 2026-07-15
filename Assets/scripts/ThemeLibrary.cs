using UnityEngine;

[CreateAssetMenu(fileName = "ThemeLibrary", menuName = "CardGame/Theme Library", order = 2)]
public class ThemeLibrary : ScriptableObject
{
    [Header("Index by Element enum (Fire=0, Water=1, Air=2, Earth=3)")]
    public ElementTheme[] Themes = new ElementTheme[4];

    public ElementTheme GetTheme(Element element)
    {
        int idx = (int)element;
        if (Themes == null || idx < 0 || idx >= Themes.Length) return null;
        return Themes[idx];
    }
}
