using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class DeckLibraryGeneratorWindow : EditorWindow
{
    [Header("Inputs")] 
    private TextAsset csvAsset;
    private DefaultAsset outputFolder;
    private ElementDeckLibrary targetLibrary;
    private int idStart = 1000;

    [MenuItem("CardGame/Deck & Library Generator")] 
    public static void ShowWindow()
    {
        var w = GetWindow<DeckLibraryGeneratorWindow>(true, "Deck & Library Generator", true);
        w.minSize = new Vector2(520, 300);
        w.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("CSV Input (deck,name,attack,health,tribute,duplicateCount)", EditorStyles.boldLabel);
        csvAsset = (TextAsset)EditorGUILayout.ObjectField("CSV", csvAsset, typeof(TextAsset), false);
        outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);
        targetLibrary = (ElementDeckLibrary)EditorGUILayout.ObjectField("ElementDeckLibrary (optional)", targetLibrary, typeof(ElementDeckLibrary), false);
        idStart = EditorGUILayout.IntField("Starting Id", idStart);

        EditorGUILayout.HelpBox("This will create CardDefinition assets per unique row. For each row it creates (1 + duplicateCount) copies. It then populates the ElementDeckLibrary arrays. Aim for exactly 40 per deck; the tool will warn if a deck does not equal 40. Artwork is left empty for you to assign later.", MessageType.Info);

        if (GUILayout.Button("Generate Decks & Library"))
        {
            if (csvAsset == null)
            {
                EditorUtility.DisplayDialog("Missing CSV", "Assign a CSV TextAsset.", "OK");
                return;
            }
            GenerateFromCsv(csvAsset);
        }
    }

    private class CardRow
    {
        public string Deck; // Fire/Water/Air/Earth
        public string Name;
        public int Atk;
        public int Hp;
        public int Tribute;
        public int DuplicateCount;
    }

    private void GenerateFromCsv(TextAsset csv)
    {
        string folder = GetOutputFolderPath();
        EnsureFolder(folder);

        var rows = ParseCsv(csv.text);
        // Group by deck
        var byDeck = rows.GroupBy(r => r.Deck.ToLowerInvariant());

        // If updating an existing library, start from what it already has to avoid resetting
        var fire = (targetLibrary != null && targetLibrary.FireDeck != null)
            ? new List<CardDefinition>(targetLibrary.FireDeck)
            : new List<CardDefinition>();
        var water = (targetLibrary != null && targetLibrary.WaterDeck != null)
            ? new List<CardDefinition>(targetLibrary.WaterDeck)
            : new List<CardDefinition>();
        var air = (targetLibrary != null && targetLibrary.AirDeck != null)
            ? new List<CardDefinition>(targetLibrary.AirDeck)
            : new List<CardDefinition>();
        var earth = (targetLibrary != null && targetLibrary.EarthDeck != null)
            ? new List<CardDefinition>(targetLibrary.EarthDeck)
            : new List<CardDefinition>();

        int nextId = idStart;

        foreach (var g in byDeck)
        {
            var uniques = g.ToList();
            // Create copies per unique row: 1 + DuplicateCount
            var createdForDeck = new List<CardDefinition>();
            foreach (var u in uniques)
            {
                int copies = Mathf.Max(1, 1 + u.DuplicateCount);
                for (int ci = 0; ci < copies; ci++)
                {
                    var def = CreateCardDefinition(folder, ref nextId, u.Name, u.Atk, u.Hp, u.Tribute, copyIndex: ci);
                    createdForDeck.Add(def);
                }
            }

            // After appending, we will check totals below
            if (createdForDeck.Count != 40)
            {
                Debug.LogWarning($"Deck '{g.Key}' ended with {createdForDeck.Count} cards (expected 40). Adjust duplicateCount values to total 40.");
            }

            switch (g.Key)
            {
                case "fire": fire.AddRange(createdForDeck); break;
                case "water": water.AddRange(createdForDeck); break;
                case "air": air.AddRange(createdForDeck); break;
                case "earth": earth.AddRange(createdForDeck); break;
                default: Debug.LogWarning($"Unknown deck key '{g.Key}'. Expected Fire/Water/Air/Earth."); break;
            }
        }

        // Create or update ElementDeckLibrary
        var lib = targetLibrary;
        if (lib == null)
        {
            string decksFolder = "Assets/Cards/Decks";
            EnsureFolder(decksFolder);
            string libPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(decksFolder, "ElementDeckLibrary.asset"));
            lib = ScriptableObject.CreateInstance<ElementDeckLibrary>();
            AssetDatabase.CreateAsset(lib, libPath);
        }

        // Final warnings for deck sizes
        if (fire.Count != 40) Debug.LogWarning($"Fire deck now has {fire.Count} cards; expected 40.");
        if (water.Count != 40) Debug.LogWarning($"Water deck now has {water.Count} cards; expected 40.");
        if (air.Count != 40) Debug.LogWarning($"Air deck now has {air.Count} cards; expected 40.");
        if (earth.Count != 40) Debug.LogWarning($"Earth deck now has {earth.Count} cards; expected 40.");

        lib.FireDeck = fire.ToArray();
        lib.WaterDeck = water.ToArray();
        lib.AirDeck = air.ToArray();
        lib.EarthDeck = earth.ToArray();
        EditorUtility.SetDirty(lib);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Done", "Generated CardDefinitions and updated ElementDeckLibrary.", "OK");
    }

    private static List<CardRow> ParseCsv(string text)
    {
        var list = new List<CardRow>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return list;

        int start = 0;
        // header detection
        var lower = lines[0].ToLowerInvariant();
        bool hasHeader = lower.Contains("deck") && lower.Contains("name");
        if (hasHeader) start = 1;

        for (int i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            var cols = line.Split(',');
            if (cols.Length < 5) continue;
            string deck = cols[0].Trim();
            string name = cols[1].Trim();
            int atk = TryInt(cols, 2, 1);
            int hp = TryInt(cols, 3, 1);
            int trib = TryInt(cols, 4, 0);
            int dupCount = TryInt(cols, 5, 0);
            if (string.IsNullOrEmpty(deck) || string.IsNullOrEmpty(name)) continue;
            list.Add(new CardRow { Deck = deck, Name = name, Atk = atk, Hp = hp, Tribute = trib, DuplicateCount = dupCount });
        }
        return list;
    }

    private static int TryInt(string[] cols, int index, int fallback)
    {
        if (index >= cols.Length) return fallback;
        int v; if (int.TryParse(cols[index].Trim(), out v)) return v; return fallback;
    }

    

    private static string GetOutputFolderPath(DefaultAsset folder)
    {
        if (folder == null) return "Assets/Cards/Definitions";
        var p = AssetDatabase.GetAssetPath(folder);
        return string.IsNullOrEmpty(p) ? "Assets/Cards/Definitions" : p;
    }

    private string GetOutputFolderPath()
    {
        return GetOutputFolderPath(outputFolder);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        var parts = folder.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    private static CardDefinition CreateCardDefinition(string folder, ref int nextId, string name, int atk, int hp, int trib, int copyIndex)
    {
        var def = ScriptableObject.CreateInstance<CardDefinition>();
        def.Id = nextId++;
        def.DisplayName = name;
        def.Attack = atk;
        def.Health = hp;
        def.TributeCost = trib;
        def.Artwork = null; // user will assign later

        string fileName = copyIndex > 0 ? $"Card_{Sanitize(name)}_{copyIndex}.asset" : $"Card_{Sanitize(name)}.asset";
        string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, fileName));
        AssetDatabase.CreateAsset(def, path);
        return def;
    }

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Where(c => !invalid.Contains(c)).ToArray()).Replace(' ', '_');
    }
}
