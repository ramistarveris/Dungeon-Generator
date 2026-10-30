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
    public Color fairyColor = new Color32(255, 130, 190, 255);
    public Color puzzleColor = new Color32(160, 85, 220, 255);
    public Color trapColor = new Color32(255, 145, 35, 255);
    public Color minibossColor = Color.yellow;
    public Color witherDoorColor = Color.black;
    public Color corridorColor = Color.gray;

    // Floor occupancy is an adjustable visual approximation, not a verified Hypixel rate.
    private static readonly int[] Widths = { 4, 5, 5, 6, 6, 6, 6 };
    private static readonly int[] Heights = { 5, 5, 5, 5, 5, 6, 6 };
    private static readonly float[] Densities = { .70f, .74f, .78f, .82f, .86f, .90f, 1.00f };

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    private static readonly Vector2Int[][] Shapes =
    {
        new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up, Vector2Int.one }, // 2x2
        new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.right * 2, Vector2Int.right * 3 }, // 1x4
        new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.right * 2 }, // 1x3
        new[] { Vector2Int.zero, Vector2Int.right }, // 1x2
        new[] { Vector2Int.zero } // 1x1
    };

    private static readonly string[] ShapeNames = { "2x2", "1x4", "1x3", "1x2", "1x1" };

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

        // Entrance starts on the outer boundary, never in the center.
        int side = random.Next(4);
        entrance = side switch
        {
            0 => new Vector2Int(0, random.Next(height)),
            1 => new Vector2Int(width - 1, random.Next(height)),
            2 => new Vector2Int(random.Next(width), 0),
            _ => new Vector2Int(random.Next(width), height - 1)
        };
        occupied.Add(entrance);

        // Grow a compact, approximately rectangular footprint.
        // Each new cell must touch an existing cell, preserving connectivity.
        while (occupied.Count < target)
        {
            Vector2Int bestCell = default;
            Vector2Int bestParent = default;
            float bestScore = float.NegativeInfinity;

            foreach (Vector2Int from in occupied)
            {
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int candidate = from + direction;
                    if (candidate.x < 0 || candidate.y < 0 ||
                        candidate.x >= width || candidate.y >= height ||
                        occupied.Contains(candidate))
                        continue;

                    int neighbors = 0;
                    foreach (Vector2Int adjacent in Directions)
                        if (occupied.Contains(candidate + adjacent)) neighbors++;

                    int minX = candidate.x, maxX = candidate.x;
                    int minY = candidate.y, maxY = candidate.y;
                    foreach (Vector2Int cell in occupied)
                    {
                        minX = Mathf.Min(minX, cell.x);
                        maxX = Mathf.Max(maxX, cell.x);
                        minY = Mathf.Min(minY, cell.y);
                        maxY = Mathf.Max(maxY, cell.y);
                    }

                    int spanX = maxX - minX + 1;
                    int spanY = maxY - minY + 1;
                    int emptyInBounds = spanX * spanY - (occupied.Count + 1);
                    float score = neighbors * 5f
                        - emptyInBounds * 1.5f
                        - Mathf.Abs(spanX - spanY) * 2f
                        + (float)random.NextDouble() * 4f;

                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestCell = candidate;
                    bestParent = from;
                }
            }

            occupied.Add(bestCell);
            AddConnection(bestParent, bestCell);
        }

        // Blood must also be on the outer boundary.
        boss = FindFarthestBoundaryCell(width, height);
        // Recover the critical path from Entrance to Blood using the spanning tree.
        Dictionary<Vector2Int, Vector2Int> parents = new() { [entrance] = entrance };
        Queue<Vector2Int> queue = new();
        queue.Enqueue(entrance);
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int next = current + direction;
                if (!occupied.Contains(next) || parents.ContainsKey(next) || !HasConnection(current, next)) continue;
                parents[next] = current;
                queue.Enqueue(next);
            }
        }
        List<Vector2Int> path = new();
        for (Vector2Int cell = boss; ; cell = parents[cell])
        {
            path.Add(cell);
            if (cell == entrance) break;
        }
        path.Reverse();
        HashSet<Vector2Int> critical = new(path);
        Vector2Int fairy = path.Count > 2 ? path[path.Count / 2] : entrance;
        List<Vector2Int> leaves = new();
        foreach (Vector2Int cell in occupied)
        {
            if (critical.Contains(cell)) continue;
            int degree = 0;
            foreach (Vector2Int direction in Directions)
                if (HasConnection(cell, cell + direction)) degree++;
            if (degree == 1) leaves.Add(cell);
        }
        leaves.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        for (int i = leaves.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (leaves[i], leaves[j]) = (leaves[j], leaves[i]);
        }
        Dictionary<Vector2Int, (string label, Color color)> special = new();
        if (fairy != entrance) special[fairy] = ("Fairy", fairyColor);
        int leafIndex = 0;
        for (int i = 0; i < 2 && leafIndex < leaves.Count; i++)
            special[leaves[leafIndex++]] = ("Puzzle", puzzleColor);
        if (floorLevel >= 3 && leafIndex < leaves.Count)
            special[leaves[leafIndex++]] = ("Trap", trapColor);
        if (leafIndex < leaves.Count)
            special[leaves[leafIndex++]] = ("Miniboss", minibossColor);

        // Reserve Entrance and Boss as single-cell rooms.
        HashSet<Vector2Int> remaining = new(occupied);
        remaining.Remove(entrance);
        remaining.Remove(boss);
        foreach (Vector2Int cell in special.Keys) remaining.Remove(cell);
        int roomId = 0;
        CreateRoom(roomId, "Entrance", new List<Vector2Int> { entrance }, entranceColor);
        cellRoomIds[entrance] = roomId++;
        CreateRoom(roomId, "Blood", new List<Vector2Int> { boss }, bossColor);
        cellRoomIds[boss] = roomId++;

        foreach (var entry in special)
        {
            CreateRoom(roomId, entry.Value.label, new List<Vector2Int> { entry.Key }, entry.Value.color);
            cellRoomIds[entry.Key] = roomId++;
        }

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
            {
                CreateCorridor(edge.Item1, edge.Item2);
                if (critical.Contains(edge.Item1) && critical.Contains(edge.Item2))
                {
                    if (edge.Item1 == boss || edge.Item2 == boss)
                        CreateDoor(edge.Item1, edge.Item2, "BloodDoor", bossColor);
                    else if (random.NextDouble() < .35)
                        CreateDoor(edge.Item1, edge.Item2, "WitherDoor", witherDoorColor);
                }
            }
        }

        Debug.Log($"F{floorLevel}: {roomId} rooms, {occupied.Count}/{width * height} cells, seed {seed}");
    }

    private bool HasConnection(Vector2Int a, Vector2Int b)
    {
        if (a.x > b.x || (a.x == b.x && a.y > b.y)) (a, b) = (b, a);
        return connections.Contains((a, b));
    }

    private void CreateDoor(Vector2Int a, Vector2Int b, string label, Color color)
    {
        GameObject door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = label;
        door.transform.SetParent(transform, false);
        generated.Add(door);
        door.transform.localPosition = new Vector3((a.x + b.x) * roomSpacing / 2f, .35f,
            (a.y + b.y) * roomSpacing / 2f);
        door.transform.localScale = a.x != b.x
            ? new Vector3(.2f, 1f, corridorWidth)
            : new Vector3(corridorWidth, 1f, .2f);
        SetColor(door, color);
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

    private Vector2Int FindFarthestBoundaryCell(int width, int height)
    {
        Queue<Vector2Int> queue = new();
        Dictionary<Vector2Int, int> distances = new() { [entrance] = 0 };
        queue.Enqueue(entrance);
        Vector2Int farthest = entrance;
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            bool boundary = current.x == 0 || current.y == 0 ||
                current.x == width - 1 || current.y == height - 1;
            if (boundary && distances[current] > distances[farthest])
                farthest = current;
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int next = current + direction;
                if (!occupied.Contains(next) || distances.ContainsKey(next) ||
                    !HasConnection(current, next)) continue;
                distances[next] = distances[current] + 1;
                queue.Enqueue(next);
            }
        }
        return farthest;
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
