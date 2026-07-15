using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class CardDefinitionGeneratorWindow : EditorWindow
{
    private DefaultAsset outputFolder;
    private int idStart = 100;
    private int defaultAttack = 1;
    private int defaultHealth = 1;
    private int defaultTribute = 0;
    private TextAsset csvAsset;

    [MenuItem("CardGame/Card Definition Generator")] 
    public static void ShowWindow()
    {
        var w = GetWindow<CardDefinitionGeneratorWindow>(true, "Card Definition Generator", true);
        w.minSize = new Vector2(420, 300);
        w.Show();
    }

    private void OnGUI()
    {
        outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);
        idStart = EditorGUILayout.IntField("Starting Id", idStart);
        defaultAttack = EditorGUILayout.IntField("Default Attack", defaultAttack);
        defaultHealth = EditorGUILayout.IntField("Default Health", defaultHealth);
        defaultTribute = EditorGUILayout.IntField("Default TributeCost", defaultTribute);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Generate From Selected Sprites", EditorStyles.boldLabel);
        if (GUILayout.Button("Generate For Selected Sprites"))
        {
            var sprites = Selection.GetFiltered<Sprite>(SelectionMode.Assets);
            if (sprites == null || sprites.Length == 0)
            {
                EditorUtility.DisplayDialog("No Sprites", "Select sprite assets in the Project window.", "OK");
            }
            else
            {
                GenerateFromSprites(sprites);
            }
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Generate From CSV", EditorStyles.boldLabel);
        csvAsset = (TextAsset)EditorGUILayout.ObjectField("CSV", csvAsset, typeof(TextAsset), false);
        if (GUILayout.Button("Generate From CSV"))
        {
            if (csvAsset == null)
            {
                EditorUtility.DisplayDialog("No CSV", "Assign a CSV TextAsset.", "OK");
            }
            else
            {
                GenerateFromCsv(csvAsset);
            }
        }
    }

    private string GetOutputFolderPath()
    {
        if (outputFolder == null)
            return "Assets/Cards/Definitions";
        var p = AssetDatabase.GetAssetPath(outputFolder);
        if (string.IsNullOrEmpty(p)) return "Assets/Cards/Definitions";
        return p;
    }

    private void GenerateFromSprites(Sprite[] sprites)
    {
        string folder = GetOutputFolderPath();
        if (!AssetDatabase.IsValidFolder(folder))
        {
            var parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        int id = idStart;
        foreach (var s in sprites)
        {
            if (s == null) continue;
            var def = ScriptableObject.CreateInstance<CardDefinition>();
            def.Id = id++;
            def.DisplayName = s.name;
            def.Attack = defaultAttack;
            def.Health = defaultHealth;
            def.TributeCost = defaultTribute;
            def.Artwork = s;

            string fileName = "Card_" + Sanitize(s.name) + ".asset";
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, fileName));
            AssetDatabase.CreateAsset(def, path);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Done", "Generated CardDefinitions: " + sprites.Length, "OK");
    }

    private void GenerateFromCsv(TextAsset csv)
    {
        string folder = GetOutputFolderPath();
        if (!AssetDatabase.IsValidFolder(folder))
        {
            var parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        var lines = csv.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return;
        int start = 0;
        var first = lines[0].ToLowerInvariant();
        if (first.Contains("id") && first.Contains("name"))
        {
            start = 1;
        }

        int created = 0;
        for (int i = start; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length < 2) continue;

            int id = SafeInt(cols, 0, idStart + (i - start));
            string name = SafeString(cols, 1, "Card" + id);
            int atk = SafeInt(cols, 2, defaultAttack);
            int hp = SafeInt(cols, 3, defaultHealth);
            int trib = SafeInt(cols, 4, defaultTribute);
            string spriteRef = cols.Length > 5 ? cols[5].Trim() : string.Empty;

            Sprite sprite = null;
            if (!string.IsNullOrEmpty(spriteRef))
            {
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spriteRef);
                if (sprite == null)
                {
                    var guids = AssetDatabase.FindAssets("t:Sprite " + Path.GetFileNameWithoutExtension(spriteRef));
                    if (guids != null && guids.Length > 0)
                    {
                        var sp = AssetDatabase.GUIDToAssetPath(guids[0]);
                        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(sp);
                    }
                }
            }

            var def = ScriptableObject.CreateInstance<CardDefinition>();
            def.Id = id;
            def.DisplayName = name;
            def.Attack = atk;
            def.Health = hp;
            def.TributeCost = trib;
            def.Artwork = sprite;

            string fileName = "Card_" + Sanitize(name) + ".asset";
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, fileName));
            AssetDatabase.CreateAsset(def, path);
            created++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Done", "Generated from CSV: " + created, "OK");
    }

    private static int SafeInt(string[] cols, int index, int fallback)
    {
        if (index >= cols.Length) return fallback;
        int v;
        if (int.TryParse(cols[index].Trim(), out v)) return v;
        return fallback;
    }

    private static string SafeString(string[] cols, int index, string fallback)
    {
        if (index >= cols.Length) return fallback;
        var s = cols[index].Trim();
        return string.IsNullOrEmpty(s) ? fallback : s;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return clean.Replace(' ', '_');
    }
}
