using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class CardDefinitionsGeneratorWindow : EditorWindow
{
    public enum ElementChoice { Fire, Water, Air, Earth }
    [Header("Generate ONE element per run")]
    public ElementChoice Element = ElementChoice.Fire;
    public TextAsset Csv;

    [Header("Output folder for generated CardDefinition assets")]
    public DefaultAsset OutputFolder;

    [Header("Library to update")] 
    public ElementDeckLibrary DeckLibrary;

    [Header("ID assignment")]
    public int StartingId = 100;

    [MenuItem("Tools/Cards/Card Definitions Generator")] 
    public static void Open()
    {
        var w = GetWindow<CardDefinitionsGeneratorWindow>(true, "Card Definitions Generator");
        w.minSize = new Vector2(420, 360);
        w.Show();
    }

    private void OnGUI()
    {
        GUILayout.Label("CSV Input", EditorStyles.boldLabel);
        Element = (ElementChoice)EditorGUILayout.EnumPopup("Element", Element);
        Csv = (TextAsset)EditorGUILayout.ObjectField("CSV", Csv, typeof(TextAsset), false);

        GUILayout.Space(8);
        GUILayout.Label("Output & Library", EditorStyles.boldLabel);
        OutputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", OutputFolder, typeof(DefaultAsset), false);
        DeckLibrary = (ElementDeckLibrary)EditorGUILayout.ObjectField("ElementDeckLibrary", DeckLibrary, typeof(ElementDeckLibrary), false);
        StartingId = EditorGUILayout.IntField("Starting Id", StartingId);

        GUILayout.Space(8);
        using (new EditorGUI.DisabledScope(OutputFolder == null || DeckLibrary == null || Csv == null))
        {
            if (GUILayout.Button("Generate Definitions and Update Library", GUILayout.Height(32)))
            {
                Generate();
            }
        }

        EditorGUILayout.HelpBox("Generates ONE deck at a time. CSV headers: name, attack, health, tribute, duplicate. IDs auto-assigned from 'Starting Id'. Overwrite policy: skip existing IDs.", MessageType.Info);
    }

    private void Generate()
    {
        string folder = AssetDatabase.GetAssetPath(OutputFolder);
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
        {
            EditorUtility.DisplayDialog("Invalid Folder", "Please assign a valid output folder.", "OK");
            return;
        }

        var existingById = LoadExistingById(folder);

        int nextId = StartingId;
        var generated = ImportCsv(Csv, folder, existingById, ref nextId).OrderBy(d => d.Id).ToArray();

        // Update library (skip nulls, sort by Id)
        if (DeckLibrary != null)
        {
            switch (Element)
            {
                case ElementChoice.Fire: DeckLibrary.FireDeck = generated; break;
                case ElementChoice.Water: DeckLibrary.WaterDeck = generated; break;
                case ElementChoice.Air: DeckLibrary.AirDeck = generated; break;
                case ElementChoice.Earth: DeckLibrary.EarthDeck = generated; break;
            }
            EditorUtility.SetDirty(DeckLibrary);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Done", "Card definitions generated and library updated.", "OK");
    }

    private Dictionary<int, CardDefinition> LoadExistingById(string folder)
    {
        var dict = new Dictionary<int, CardDefinition>();
        string[] guids = AssetDatabase.FindAssets("t:CardDefinition", new[] { folder });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var def = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
            if (def == null) continue;
            if (!dict.ContainsKey(def.Id)) dict.Add(def.Id, def);
        }
        return dict;
    }

    private IEnumerable<CardDefinition> ImportCsv(TextAsset csv, string folder, Dictionary<int, CardDefinition> existingById, ref int nextId)
    {
        var list = new List<CardDefinition>();
        if (csv == null) return list;

        using (var reader = new StringReader(csv.text))
        {
            string headerLine = reader.ReadLine();
            if (string.IsNullOrEmpty(headerLine)) return list;
            var headers = SplitCsv(headerLine);
            int idxName = IndexOf(headers, "name"); if (idxName < 0) idxName = IndexOf(headers, "display name");
            int idxAtk = IndexOf(headers, "attack");
            int idxHp = IndexOf(headers, "health");
            int idxTrib = IndexOf(headers, "tribute"); if (idxTrib < 0) idxTrib = IndexOf(headers, "tribute cost");
            int idxDup = IndexOf(headers, "duplicate");
            if (idxName < 0 || idxAtk < 0 || idxHp < 0 || idxTrib < 0)
            {
                EditorUtility.DisplayDialog("Bad CSV", "Missing one or more required headers: name, attack, health, tribute, duplicate (duplicate optional)", "OK");
                return list;
            }

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = SplitCsv(line);
                if (cols.Count <= Math.Max(idxName, Math.Max(idxAtk, Math.Max(idxHp, Math.Max(idxTrib, Math.Max(idxDup, 0)))))) continue;
                string displayName = cols[idxName]?.Trim();
                if (!TryParseInt(cols[idxAtk], out int atk)) atk = 0;
                if (!TryParseInt(cols[idxHp], out int hp)) hp = 0;
                if (!TryParseInt(cols[idxTrib], out int trib)) trib = 0;
                int dup = 0; if (idxDup >= 0) TryParseInt(cols[idxDup], out dup); if (dup < 0) dup = 0;

                // Create 1 + dup copies with sequential free IDs starting from nextId
                int copies = 1 + dup;
                for (int n = 0; n < copies; n++)
                {
                    int id = NextFreeId(ref nextId, existingById);
                    var def = ScriptableObject.CreateInstance<CardDefinition>();
                    def.Id = id;
                    def.DisplayName = displayName;
                    def.Attack = atk;
                    def.Health = hp;
                    def.TributeCost = trib;

                    string safeName = MakeSafeFileName($"{id}_{displayName}");
                    string assetPath = Path.Combine(folder, safeName + ".asset");
                    assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                    AssetDatabase.CreateAsset(def, assetPath);
                    list.Add(def);
                    existingById[id] = def;
                }
            }
        }
        return list;
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        int i = 0; bool inQuotes = false; var cur = new System.Text.StringBuilder();
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"'); i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(cur.ToString()); cur.Length = 0;
            }
            else
            {
                cur.Append(c);
            }
            i++;
        }
        result.Add(cur.ToString());
        return result;
    }

    private static int IndexOf(List<string> headers, string name)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i]?.Trim(), name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static int NextFreeId(ref int nextId, Dictionary<int, CardDefinition> existing)
    {
        while (existing.ContainsKey(nextId)) nextId++;
        return nextId++;
    }

    private static bool TryParseInt(string s, out int value)
    {
        return int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string MakeSafeFileName(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        foreach (var ch in bad) s = s.Replace(ch, '_');
        return s.Replace(' ', '_');
    }
}
