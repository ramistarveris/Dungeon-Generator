using System;
using System.Collections.Generic;
using UnityEngine;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Dungeon Settings")]
    [Range(1, 7)] public int floorLevel = 1;
    public int seed = 12345;
    public float roomSpacing = 5f;
    [Header("Preview")]
    public bool frameMainCamera = true;

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

    // Catacombs minimum map footprints: every cell in the minimum footprint is used.
    private static readonly int[] Widths = { 4, 5, 5, 6, 6, 6, 6 };
    private static readonly int[] Heights = { 5, 5, 5, 5, 5, 6, 6 };
    private static readonly int[] PuzzleCounts = { 2, 2, 2, 3, 4, 4, 5 };

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    private static readonly Vector2Int[][] Shapes =
    {
        new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up, Vector2Int.one }, // 2x2
        new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.right * 2, Vector2Int.right * 3 }, // 1x4
        new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.right * 2 }, // 1x3
        new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up }, // L (three cells of a 2x2)
        new[] { Vector2Int.zero, Vector2Int.right }, // 1x2
        new[] { Vector2Int.zero } // 1x1
    };

    private static readonly string[] ShapeNames = { "2x2", "1x4", "1x3", "L", "1x2", "1x1" };

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
        int target = width * height;

        // Entrance is on the perimeter, facing inward. It has exactly one door.
        int side = random.Next(4);
        entrance = side switch
        {
            0 => new Vector2Int(0, random.Next(height)),
            1 => new Vector2Int(width - 1, random.Next(height)),
            2 => new Vector2Int(random.Next(width), 0),
            _ => new Vector2Int(random.Next(width), height - 1)
        };
        Vector2Int inward = side switch
        {
            0 => Vector2Int.right,
            1 => Vector2Int.left,
            2 => Vector2Int.up,
            _ => Vector2Int.down
        };
        Vector2Int firstRoomCell = entrance + inward;
        occupied.Add(entrance);
        occupied.Add(firstRoomCell);
        AddConnection(entrance, firstRoomCell);

        // Reserve a far-away boundary cell for Blood. Add it last as a leaf,
        // so neither the Start Room nor the Blood Room has a "back door".
        List<Vector2Int> bloodCandidates = new();
        int bestBloodDistance = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int candidate = new(x, y);
                bool boundary = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                if (!boundary || candidate == entrance || candidate == firstRoomCell) continue;
                int distance = Mathf.Abs(x - entrance.x) + Mathf.Abs(y - entrance.y);
                if (distance > bestBloodDistance)
                {
                    bloodCandidates.Clear();
                    bestBloodDistance = distance;
                }
                if (distance == bestBloodDistance) bloodCandidates.Add(candidate);
            }
        }
        boss = bloodCandidates[random.Next(bloodCandidates.Count)];

        // Grow the spanning tree through every cell except the reserved Blood cell.
        // Never branch from Entrance: its sole connection must point inward.
        while (occupied.Count < target - 1)
        {
            Vector2Int bestCell = default;
            Vector2Int bestParent = default;
            float bestScore = float.NegativeInfinity;
            List<Vector2Int> frontier = new(occupied);
            frontier.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            foreach (Vector2Int from in frontier)
            {
                if (from == entrance) continue;
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int candidate = from + direction;
                    if (candidate.x < 0 || candidate.y < 0 ||
                        candidate.x >= width || candidate.y >= height ||
                        candidate == boss || occupied.Contains(candidate))
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
            if (bestScore == float.NegativeInfinity)
            {
                Debug.LogError("Unable to grow dungeon layout.");
                return;
            }

            occupied.Add(bestCell);
            AddConnection(bestParent, bestCell);
        }

        // Attach Blood through one connection. Prefer the deepest available
        // neighbor to make the critical path more interesting.
        Dictionary<Vector2Int, int> depths = new() { [entrance] = 0 };
        Queue<Vector2Int> depthQueue = new();
        depthQueue.Enqueue(entrance);
        while (depthQueue.Count > 0)
        {
            Vector2Int cell = depthQueue.Dequeue();
            foreach (Vector2Int dir in Directions)
            {
                Vector2Int next = cell + dir;
                if (!occupied.Contains(next) || depths.ContainsKey(next) ||
                    !HasConnection(cell, next)) continue;
                depths[next] = depths[cell] + 1;
                depthQueue.Enqueue(next);
            }
        }
        Vector2Int bloodParent = default;
        int bestDepth = -1;
        foreach (Vector2Int dir in Directions)
        {
            Vector2Int neighbor = boss + dir;
            if (neighbor == entrance || !depths.TryGetValue(neighbor, out int depth)) continue;
            if (depth <= bestDepth) continue;
            bestDepth = depth;
            bloodParent = neighbor;
        }
        if (bestDepth < 0)
        {
            Debug.LogError("Blood Room could not be attached.");
            return;
        }
        occupied.Add(boss);
        AddConnection(bloodParent, boss);

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
        int puzzleTarget = PuzzleCounts[index]; // F1-F7: 2, 2, 2, 3, 4, 4, 5
        List<Vector2Int> puzzleCells = new(leaves);
        if (puzzleCells.Count < puzzleTarget)
        {
            foreach (Vector2Int cell in occupied)
                if (!critical.Contains(cell) && !puzzleCells.Contains(cell)) puzzleCells.Add(cell);
        }
        for (int i = 0; i < puzzleTarget && i < puzzleCells.Count; i++)
            special[puzzleCells[i]] = ("Puzzle", puzzleColor);
        while (leafIndex < leaves.Count && special.ContainsKey(leaves[leafIndex])) leafIndex++;
        if (floorLevel >= 3 && leafIndex < leaves.Count)
            special[leaves[leafIndex++]] = ("Trap", trapColor);
        while (leafIndex < leaves.Count && special.ContainsKey(leaves[leafIndex])) leafIndex++;
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

        // Tile connected multi-cell Normal Rooms globally before any 1x1 fallback.
        // A random single-cell anchor would fragment the map and almost always
        // leave the larger real room shapes unused.
        HashSet<int> usedNormalShapes = new();
        while (remaining.Count > 0)
        {
            List<(int shape, int rotation, List<Vector2Int> cells)> fits = new();
            List<Vector2Int> pivots = new(remaining);
            pivots.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            for (int shape = 0; shape < Shapes.Length - 1; shape++) // multi-cell only
            {
                for (int rotation = 0; rotation < 4; rotation++)
                {
                    foreach (Vector2Int pivot in pivots)
                    {
                        List<Vector2Int> cells = new();
                        bool valid = true;
                        foreach (Vector2Int point in Shapes[shape])
                        {
                            Vector2Int cell = pivot + Rotate(point, rotation);
                            if (!remaining.Contains(cell)) { valid = false; break; }
                            cells.Add(cell);
                        }
                        // A room may only absorb cells connected by tree edges.
                        // Geometrical adjacency alone can merge unrelated branches,
                        // creating two doors between the same room pair (a cycle).
                        if (valid && IsTreeConnected(cells))
                            fits.Add((shape, rotation, cells));
                    }
                }
            }

            if (fits.Count == 0)
            {
                Vector2Int single = pivots[random.Next(pivots.Count)];
                CreateRoom(roomId, "1x1", new List<Vector2Int> { single }, normalColor);
                cellRoomIds[single] = roomId++;
                remaining.Remove(single);
                continue;
            }

            // Favor large shapes, but also reward different shape types.
            // Rotations are enumerated explicitly (0/90/180/270 degrees).
            int bestScore = int.MinValue;
            (int shape, int rotation, List<Vector2Int> cells) chosen = default;
            foreach (var fit in fits)
            {
                int score = fit.cells.Count * 8
                    + (usedNormalShapes.Contains(fit.shape) ? 0 : 12)
                    + random.Next(16);
                if (score <= bestScore) continue;
                bestScore = score;
                chosen = fit;
            }

            CreateRoom(roomId, ShapeNames[chosen.shape], chosen.cells, normalColor, chosen.rotation);
            usedNormalShapes.Add(chosen.shape);
            foreach (Vector2Int cell in chosen.cells)
            {
                cellRoomIds[cell] = roomId;
                remaining.Remove(cell);
            }
            roomId++;
        }

        // Contract each tree-connected room into one node. The room graph
        // remains a tree: one unique Start-to-Blood route with bonus branches.
        // Door types are corridor colors; no separate door GameObjects.
        HashSet<(int, int)> roomEdges = new();
        int startConnections = 0;
        int bloodConnections = 0;
        foreach (var edge in connections)
        {
            int a = cellRoomIds[edge.Item1];
            int b = cellRoomIds[edge.Item2];
            if (a == b) continue; // Seam inside a multi-cell Normal Room.

            (int, int) roomEdge = a < b ? (a, b) : (b, a);
            if (!roomEdges.Add(roomEdge))
            {
                Debug.LogError($"Duplicate room connection {a}-{b}; layout is not a tree.");
                continue;
            }
            if (edge.Item1 == entrance || edge.Item2 == entrance) startConnections++;
            if (edge.Item1 == boss || edge.Item2 == boss) bloodConnections++;

            Color corridor = corridorColor; // Normal or Bonus Path
            if (critical.Contains(edge.Item1) && critical.Contains(edge.Item2))
            {
                if (edge.Item1 == boss || edge.Item2 == boss)
                    corridor = bossColor; // Blood Door: red corridor
                else if (edge.Item1 == entrance || edge.Item2 == entrance)
                    corridor = corridorColor; // Start Door: normal corridor
                else
                    corridor = witherDoorColor; // Wither Door: black corridor
            }
            CreateCorridor(edge.Item1, edge.Item2, corridor);
        }
        if (roomEdges.Count != roomId - 1 || startConnections != 1 || bloodConnections != 1)
            Debug.LogError($"Invalid dungeon tree: {roomEdges.Count}/{roomId - 1} " +
                $"connections, Start={startConnections}, Blood={bloodConnections}.");

        FrameMainCamera(width, height);
        List<string> shapeSummary = new();
        foreach (int shape in usedNormalShapes) shapeSummary.Add(ShapeNames[shape]);
        shapeSummary.Sort();
        Debug.Log($"F{floorLevel}: {roomId} rooms, {occupied.Count}/{width * height} cells, " +
            $"normal shapes: {string.Join(", ", shapeSummary)}, seed {seed}");
    }

    private bool HasConnection(Vector2Int a, Vector2Int b)
    {
        if (a.x > b.x || (a.x == b.x && a.y > b.y)) (a, b) = (b, a);
        return connections.Contains((a, b));
    }

    // Every cell of a multi-cell room must be connected using the
    // original spanning-tree edges, not merely touching geometrically.
    private bool IsTreeConnected(List<Vector2Int> cells)
    {
        if (cells.Count <= 1) return true;
        HashSet<Vector2Int> group = new(cells);
        HashSet<Vector2Int> visited = new() { cells[0] };
        Queue<Vector2Int> queue = new();
        queue.Enqueue(cells[0]);
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int next = cell + direction;
                if (group.Contains(next) && !visited.Contains(next) &&
                    HasConnection(cell, next))
                {
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
        }
        return visited.Count == cells.Count;
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

    private void CreateRoom(int id, string shape, List<Vector2Int> cells, Color color, int rotation = 0)
    {
        GameObject parent = new($"Room_{id}_{shape}_R{rotation * 90}");
        parent.transform.SetParent(transform, false);
        generated.Add(parent);

        // Expand each tile to its neighbor inside the same room, leaving a
        // gap only around the room perimeter. Multi-cell rooms therefore
        // render as a single connected footprint rather than separate squares.
        HashSet<Vector2Int> members = new(cells);
        float spacing = Mathf.Max(roomSpacing, .1f);
        float border = Mathf.Clamp((spacing - roomSize) * .5f, .025f, spacing * .45f);
        foreach (Vector2Int cell in cells)
        {
            float cx = cell.x * spacing;
            float cz = cell.y * spacing;
            float xMin = cx - spacing * .5f + (members.Contains(cell + Vector2Int.left) ? 0f : border);
            float xMax = cx + spacing * .5f - (members.Contains(cell + Vector2Int.right) ? 0f : border);
            float zMin = cz - spacing * .5f + (members.Contains(cell + Vector2Int.down) ? 0f : border);
            float zMax = cz + spacing * .5f - (members.Contains(cell + Vector2Int.up) ? 0f : border);

            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = $"Tile_{cell.x}_{cell.y}";
            tile.transform.SetParent(parent.transform, false);
            tile.transform.localPosition = new Vector3((xMin + xMax) * .5f, 0, (zMin + zMax) * .5f);
            tile.transform.localScale = new Vector3(xMax - xMin, .5f, zMax - zMin);
            SetColor(tile, color);
        }
    }

    private void CreateCorridor(Vector2Int a, Vector2Int b, Color color)
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
        SetColor(corridor, color);
    }

    private void FrameMainCamera(int width, int height)
    {
        if (!frameMainCamera || Camera.main == null) return;
        Camera camera = Camera.main;
        camera.orthographic = true;
        camera.transform.position = new Vector3((width - 1) * roomSpacing * .5f,
            Mathf.Max(width, height) * roomSpacing * 1.5f,
            (height - 1) * roomSpacing * .5f);
        camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        float verticalHalf = height * roomSpacing * .5f;
        float horizontalHalf = width * roomSpacing / (2f * Mathf.Max(.1f, camera.aspect));
        camera.orthographicSize = Mathf.Max(verticalHalf, horizontalHalf) + roomSpacing;
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
