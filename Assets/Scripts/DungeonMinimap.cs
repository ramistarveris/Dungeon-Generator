using System.Collections.Generic;
using UnityEngine;

public readonly struct DungeonCorridorInfo
{
    public readonly Vector2Int From;
    public readonly Vector2Int To;
    public readonly Color Color;

    public DungeonCorridorInfo(Vector2Int from, Vector2Int to, Color color)
    {
        From = from;
        To = to;
        Color = color;
    }
}

// IMGUI minimap: no Canvas setup or additional scene assets are needed.
public sealed class DungeonMinimap : MonoBehaviour
{
    private int columns;
    private int rows;
    private int floor;
    private int activeSeed;
    private float worldSpacing;
    private Transform target;
    private Dictionary<Vector2Int, int> roomCells;
    private Dictionary<int, Color> roomColors;
    private List<DungeonCorridorInfo> corridors;

    public void Initialize(int mapColumns, int mapRows, int currentFloor, int seed,
        float spacing, Transform player, Dictionary<Vector2Int, int> cells,
        Dictionary<int, Color> colors, List<DungeonCorridorInfo> links)
    {
        columns = mapColumns;
        rows = mapRows;
        floor = currentFloor;
        activeSeed = seed;
        worldSpacing = spacing;
        target = player;
        roomCells = new Dictionary<Vector2Int, int>(cells);
        roomColors = new Dictionary<int, Color>(colors);
        corridors = new List<DungeonCorridorInfo>(links);
    }

    private void OnGUI()
    {
        if (target == null || roomCells == null || columns <= 0 || rows <= 0)
            return;

        float panelWidth = Mathf.Clamp(Screen.width * .24f, 206f, 280f);
        float cell = Mathf.Min(35f, (panelWidth - 28f) / columns);
        float mapWidth = columns * cell;
        float mapHeight = rows * cell;
        Rect panel = new(12f, 12f, panelWidth, mapHeight + 91f);
        float mapX = panel.x + (panel.width - mapWidth) * .5f;
        float mapY = panel.y + 38f;

        Fill(panel, new Color(.045f, .055f, .07f, .88f));
        GUIStyle heading = new(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };
        GUI.Label(new Rect(panel.x + 12f, panel.y + 7f, panel.width - 18f, 24f),
            $"F{floor}  |  SEED {activeSeed}", heading);

        Rect mapBackground = new(mapX - 4f, mapY - 4f,
            mapWidth + 8f, mapHeight + 8f);
        Fill(mapBackground, new Color(0f, 0f, 0f, .80f));

        // Draw paths first, behind the room tiles.
        foreach (DungeonCorridorInfo link in corridors)
        {
            Vector2 a = CellCenter(link.From, mapX, mapY, cell);
            Vector2 b = CellCenter(link.To, mapX, mapY, cell);
            float thick = cell * .19f;
            Rect passage = Mathf.Abs(a.x - b.x) > .01f
                ? new Rect(Mathf.Min(a.x, b.x), a.y - thick * .5f,
                    Mathf.Abs(a.x - b.x), thick)
                : new Rect(a.x - thick * .5f, Mathf.Min(a.y, b.y),
                    thick, Mathf.Abs(a.y - b.y));
            Fill(passage, new Color(.65f, .65f, .65f, .9f));
            Rect inner = new(passage.x + (passage.width > passage.height ? 0f : 2f),
                passage.y + (passage.height > passage.width ? 0f : 2f),
                passage.width - (passage.width > passage.height ? 0f : 4f),
                passage.height - (passage.height > passage.width ? 0f : 4f));
            Fill(inner, link.Color);
        }

        // Fill rooms and internal seams so multi-cell rooms appear connected.
        float tileSize = cell * .78f;
        foreach (KeyValuePair<Vector2Int, int> entry in roomCells)
        {
            Vector2 center = CellCenter(entry.Key, mapX, mapY, cell);
            Color roomColor = roomColors.TryGetValue(entry.Value, out Color found)
                ? found : Color.gray;
            Fill(new Rect(center.x - tileSize * .5f, center.y - tileSize * .5f,
                tileSize, tileSize), roomColor);

            if (roomCells.TryGetValue(entry.Key + Vector2Int.right, out int right) &&
                right == entry.Value)
                Fill(new Rect(center.x, center.y - tileSize * .5f,
                    cell, tileSize), roomColor);

            if (roomCells.TryGetValue(entry.Key + Vector2Int.up, out int up) &&
                up == entry.Value)
                Fill(new Rect(center.x - tileSize * .5f, center.y - cell,
                    tileSize, cell), roomColor);
        }

        // Coordinates are based on the player's actual 3D world position.
        float x = mapX + (target.position.x / worldSpacing + .5f) * cell;
        float y = mapY + (rows - .5f - target.position.z / worldSpacing) * cell;
        Vector2 marker = new(x, y);
        Fill(new Rect(x - 5f, y - 5f, 10f, 10f), Color.white);
        Matrix4x4 oldMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(target.eulerAngles.y, marker);
        GUIStyle arrow = new(GUI.skin.label)
        {
            fontSize = 19,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.cyan }
        };
        GUI.Label(new Rect(x - 13f, y - 17f, 26f, 26f), "▲", arrow);
        GUI.matrix = oldMatrix;

        GUIStyle hint = new(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(.87f, .88f, .90f) }
        };
        GUI.Label(new Rect(panel.x + 11f, mapY + mapHeight + 10f,
            panel.width - 18f, 18f), "WASD Move  |  Shift Run  |  Space Jump", hint);
        GUI.Label(new Rect(panel.x + 11f, mapY + mapHeight + 25f,
            panel.width - 18f, 18f), "Mouse Look  |  Esc Unlock  |  Click Lock", hint);
    }

    private Vector2 CellCenter(Vector2Int grid, float x, float y, float cell)
    {
        return new Vector2(x + (grid.x + .5f) * cell,
            y + (rows - .5f - grid.y) * cell);
    }

    private static void Fill(Rect rect, Color tint)
    {
        Color previous = GUI.color;
        GUI.color = tint;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
