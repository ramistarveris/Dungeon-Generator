using System;
using System.Collections.Generic;
using UnityEngine;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Dungeon Settings")]
    [Range(1, 7)] public int floorLevel = 1;
    public int seed = 12345;
    public float roomSpacing = 5f;

    [Header("Visual Settings")]
    public float roomSize = 4f;
    public float corridorWidth = 1.5f;
    public Color normalColor = new Color32(153, 76, 0, 255);
    public Color entranceColor = Color.green;
    public Color bossColor = Color.red;
    public Color corridorColor = Color.gray;

    // Floor occupancy is an adjustable visual approximation, not a verified Hypixel rate.
    private static readonly int[] Widths = { 4, 5, 5, 6, 6, 6, 6 };
    private static readonly int[] Heights = { 5, 5, 5, 5, 5, 6, 6 };
    private static readonly float[] Densities = { .70f, .74f, .78f, .82f, .86f, .90f, .95f };

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    private static readonly Vector2Int[][] Shapes =
    {
        new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up, Vector2Int.one }, // 2x2
        new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.right * 2, Vector2Int.right * 3 }, // 1x4
        new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.right * 2 }, // 1x3
        new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up }, // 1x3 L
        new[] { Vector2Int.zero, Vector2Int.right }, // 1x2
        new[] { Vector2Int.zero } // 1x1
    };

    private static readonly string[] ShapeNames = { "2x2", "1x4", "1x3", "1x3_L", "1x2", "1x1" };

    private readonly HashSet<Vector2Int> occupied = new();
    private readonly HashSet<(Vector2Int, Vector2Int)> connections = new();
    private readonly Dictionary<Vector2Int, int> cellRoomIds = new();
    private readonly List<GameObject> generated = new();
    private System.Random random;
    private Vector2Int entrance;
    private Vector2Int boss;

    private void Start() => Generate();

    [ContextMenu("Generate Dungeon")]
    public void Generate()
    {
        ClearGenerated();
        occupied.Clear();
        connections.Clear();
        cellRoomIds.Clear();
        random = new System.Random(seed);

        int index = Mathf.Clamp(floorLevel, 1, 7) - 1;
        int width = Widths[index];
        int height = Heights[index];
        int target = Mathf.Clamp(Mathf.RoundToInt(width * height * Densities[index]), 2, width * height);

        entrance = new Vector2Int(width / 2, height / 2);
        occupied.Add(entrance);

        // A randomized frontier grows one connected dungeon without a walk timeout.
        List<Vector2Int> frontier = new() { entrance };
        while (occupied.Count < target)
        {
            Vector2Int from = frontier[random.Next(frontier.Count)];
            List<Vector2Int> options = new();
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int next = from + direction;
                if (next.x >= 0 && next.y >= 0 && next.x < width && next.y < height && !occupied.Contains(next))
                    options.Add(next);
            }

            if (options.Count == 0)
            {
                frontier.Remove(from);
                continue;
            }

            Vector2Int added = options[random.Next(options.Count)];
            occupied.Add(added);
            frontier.Add(added);
            AddConnection(from, added);
        }

        boss = FindFarthestCell();

        // Reserve Entrance and Boss as single-cell rooms.
        HashSet<Vector2Int> remaining = new(occupied);
        remaining.Remove(entrance);
        remaining.Remove(boss);
        int roomId = 0;
        CreateRoom(roomId, "Entrance", new List<Vector2Int> { entrance }, entranceColor);
        cellRoomIds[entrance] = roomId++;
        CreateRoom(roomId, "Boss", new List<Vector2Int> { boss }, bossColor);
        cellRoomIds[boss] = roomId++;

        while (remaining.Count > 0)
        {
            List<Vector2Int> candidates = new(remaining);
            Vector2Int anchor = candidates[random.Next(candidates.Count)];
            List<(int shape, List<Vector2Int> cells)> fits = new();

            for (int shape = 0; shape < Shapes.Length; shape++)
            {
                for (int rotation = 0; rotation < 4; rotation++)
                {
                    foreach (Vector2Int offset in Shapes[shape])
                    {
                        Vector2Int pivot = anchor - Rotate(offset, rotation);
                        List<Vector2Int> cells = new();
                        bool valid = true;
                        foreach (Vector2Int point in Shapes[shape])
                        {
                            Vector2Int cell = pivot + Rotate(point, rotation);
                            if (!remaining.Contains(cell)) { valid = false; break; }
                            cells.Add(cell);
                        }
                        if (valid) fits.Add((shape, cells));
                    }
                }
            }

            // Prefer larger shapes while retaining variation.
            fits.Sort((a, b) => b.cells.Count.CompareTo(a.cells.Count));
            int pool = Mathf.Min(fits.Count, 8);
            var chosen = fits[random.Next(pool)];
            CreateRoom(roomId, ShapeNames[chosen.shape], chosen.cells, normalColor);
            foreach (Vector2Int cell in chosen.cells)
            {
                cellRoomIds[cell] = roomId;
                remaining.Remove(cell);
            }
            roomId++;
        }

        // Add corridors only across room boundaries. The spanning-tree edges
        // guarantee a path from Entrance to Boss even with multi-cell rooms.
        foreach (var edge in connections)
        {
            if (cellRoomIds[edge.Item1] != cellRoomIds[edge.Item2])
                CreateCorridor(edge.Item1, edge.Item2);
        }

        Debug.Log($"F{floorLevel}: {roomId} rooms, {occupied.Count}/{width * height} cells, seed {seed}");
    }

    private void ClearGenerated()
    {
        foreach (GameObject obj in generated)
        {
            if (obj == null) continue;
            obj.SetActive(false);
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
        generated.Clear();
    }

    private static Vector2Int Rotate(Vector2Int p, int turns) => turns switch
    {
        1 => new Vector2Int(-p.y, p.x),
        2 => new Vector2Int(-p.x, -p.y),
        3 => new Vector2Int(p.y, -p.x),
        _ => p
    };

    private void AddConnection(Vector2Int a, Vector2Int b)
    {
        if (a.x > b.x || (a.x == b.x && a.y > b.y)) (a, b) = (b, a);
        connections.Add((a, b));
    }

    private Vector2Int FindFarthestCell()
    {
        Queue<Vector2Int> queue = new();
        Dictionary<Vector2Int, int> distance = new() { [entrance] = 0 };
        queue.Enqueue(entrance);
        Vector2Int farthest = entrance;
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int next = current + direction;
                if (!occupied.Contains(next) || distance.ContainsKey(next)) continue;
                Vector2Int a = current, b = next;
                if (a.x > b.x || (a.x == b.x && a.y > b.y)) (a, b) = (b, a);
                if (!connections.Contains((a, b))) continue;
                distance[next] = distance[current] + 1;
                queue.Enqueue(next);
                if (distance[next] > distance[farthest]) farthest = next;
            }
        }
        return farthest;
    }

    private void CreateRoom(int id, string shape, List<Vector2Int> cells, Color color)
    {
        GameObject parent = new($"Room_{id}_{shape}");
        parent.transform.SetParent(transform, false);
        generated.Add(parent);
        foreach (Vector2Int cell in cells)
        {
            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = $"Tile_{cell.x}_{cell.y}";
            tile.transform.SetParent(parent.transform, false);
            tile.transform.localPosition = new Vector3(cell.x * roomSpacing, 0, cell.y * roomSpacing);
            tile.transform.localScale = new Vector3(roomSize, .5f, roomSize);
            SetColor(tile, color);
        }
    }

    private void CreateCorridor(Vector2Int a, Vector2Int b)
    {
        GameObject corridor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        corridor.name = $"Corridor_{a.x}_{a.y}_to_{b.x}_{b.y}";
        corridor.transform.SetParent(transform, false);
        generated.Add(corridor);
        Vector3 posA = new(a.x * roomSpacing, 0, a.y * roomSpacing);
        Vector3 posB = new(b.x * roomSpacing, 0, b.y * roomSpacing);
        corridor.transform.localPosition = (posA + posB) / 2f;
        corridor.transform.localScale = a.x != b.x
            ? new Vector3(roomSpacing, .3f, corridorWidth)
            : new Vector3(corridorWidth, .3f, roomSpacing);
        SetColor(corridor, corridorColor);
    }

    private static void SetColor(GameObject obj, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return;
        Material material = new(shader) { color = color };
        obj.GetComponent<Renderer>().material = material;
    }
}
