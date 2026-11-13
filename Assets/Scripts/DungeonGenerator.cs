using System;
using System.Collections.Generic;
using UnityEngine;

public enum DungeonGenerationMode
{
    DungeonMap,
    Dungeon
}

public class DungeonGenerator : MonoBehaviour
{
    [Header("Dungeon Settings")]
    [Range(1, 7)] public int floorLevel = 1;
    public int seed = 12345;
    public float roomSpacing = 5f;

    [Header("Generation Mode")]
    public DungeonGenerationMode generationMode = DungeonGenerationMode.DungeonMap;

    [Header("Dungeon Mode - 3D Geometry")]
    [Min(8f)] public float dungeonCellSpacing = 26f;
    [Min(4f)] public float dungeonRoomSize = 20f;
    [Min(1f)] public float dungeonCorridorWidth = 5f;
    [Min(1f)] public float dungeonWallHeight = 6f;
    [Min(.1f)] public float dungeonWallThickness = .8f;
    public bool generateCeilings = false;
    public Color dungeonFloorColor = new Color32(107, 104, 95, 255);
    public Color dungeonWallColor = new Color32(73, 73, 77, 255);
    public Color dungeonAccentColor = new Color32(128, 115, 92, 255);

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
    private readonly Dictionary<uint, Material> generatedMaterials = new();
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
        foreach (Material material in generatedMaterials.Values)
        {
            if (material == null) continue;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
        generatedMaterials.Clear();
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

    private float WorldSpacing => generationMode == DungeonGenerationMode.Dungeon
        ? Mathf.Max(8f, dungeonCellSpacing) : Mathf.Max(.5f, roomSpacing);

    private float DungeonSpan => Mathf.Clamp(dungeonRoomSize, 4f, WorldSpacing - 1f);

    private float DungeonPassageWidth => Mathf.Clamp(dungeonCorridorWidth, 1f, DungeonSpan * .75f);

    private void CreateRoom(int id, string shape, List<Vector2Int> cells, Color color, int rotation = 0)
    {
        GameObject parent = new($"Room_{id}_{shape}_R{rotation * 90}");
        parent.transform.SetParent(transform, false);
        generated.Add(parent);

        if (generationMode == DungeonGenerationMode.DungeonMap)
            CreateMapRoom(parent.transform, cells, color);
        else
            CreateDungeonRoom(parent.transform, id, shape, cells, color);
    }

    // Fixed-size cells plus internal bridges: no enlarged/offset center tile
    // on an L-shaped room. Adjacent cells share a seamless footprint.
    private void CreateMapRoom(Transform parent, List<Vector2Int> cells, Color color)
    {
        float spacing = WorldSpacing;
        float size = Mathf.Clamp(roomSize, .2f, spacing - .1f);
        foreach (Vector2Int cell in cells)
        {
            CreateBlock(parent, $"MapTile_{cell.x}_{cell.y}",
                new Vector3(cell.x * spacing, 0f, cell.y * spacing),
                new Vector3(size, .5f, size), color);
        }
        JoinRoomCells(parent, cells, spacing, size, .5f, 0f, color);
    }

    // Primitive-based 3D prototype: traversable floors, walls with proper
    // openings, corridors, varied rubble/pillars and optional ceilings.
    // Geometry is based on the exact same room graph as DungeonMap.
    private void CreateDungeonRoom(Transform parent, int id, string shape,
        List<Vector2Int> cells, Color mapColor)
    {
        float spacing = WorldSpacing;
        float size = DungeonSpan;
        float wallHeight = Mathf.Max(1f, dungeonWallHeight);
        float thickness = Mathf.Clamp(dungeonWallThickness, .1f, size * .15f);
        float passageWidth = DungeonPassageWidth;
        float floorThickness = .65f;
        HashSet<Vector2Int> members = new(cells);
        Color floorColor = Color.Lerp(dungeonFloorColor, mapColor, .12f);

        foreach (Vector2Int cell in cells)
        {
            Vector3 center = new(cell.x * spacing, 0f, cell.y * spacing);
            CreateBlock(parent, $"Floor_{cell.x}_{cell.y}",
                center + Vector3.down * (floorThickness * .5f),
                new Vector3(size, floorThickness, size), floorColor);

            if (generateCeilings)
                CreateBlock(parent, $"Ceiling_{cell.x}_{cell.y}",
                    center + Vector3.up * (wallHeight + thickness * .5f),
                    new Vector3(size, thickness, size), dungeonWallColor);

            foreach (Vector2Int direction in Directions)
            {
                if (members.Contains(cell + direction)) continue;
                bool doorway = HasConnection(cell, cell + direction);
                CreateDungeonWall(parent, cell, direction, size, thickness,
                    wallHeight, doorway ? passageWidth : 0f);
            }
        }

        JoinRoomCells(parent, cells, spacing, size, floorThickness,
            -floorThickness * .5f, floorColor);
        if (generateCeilings)
            JoinRoomCells(parent, cells, spacing, size, thickness,
                wallHeight + thickness * .5f, dungeonWallColor);

        // Repeatable low obstacles placed near corners, away from door axes.
        // This gives rooms geometry beyond a flat map without blocking routes.
        System.Random terrainRandom = new(unchecked(seed * 397 ^ id * 7919));
        foreach (Vector2Int cell in cells)
        {
            if (shape == "Entrance" || shape == "Blood" || shape == "Fairy")
                continue;

            int count = terrainRandom.Next(1, 4);
            for (int i = 0; i < count; i++)
            {
                float sx = terrainRandom.Next(2) == 0 ? -1f : 1f;
                float sz = terrainRandom.Next(2) == 0 ? -1f : 1f;
                float x = (cell.x * spacing) + sx * size * (.29f + (float)terrainRandom.NextDouble() * .06f);
                float z = (cell.y * spacing) + sz * size * (.29f + (float)terrainRandom.NextDouble() * .06f);
                float width = size * (.08f + (float)terrainRandom.NextDouble() * .07f);
                float height = .35f + (float)terrainRandom.NextDouble() * .85f;
                CreateBlock(parent, $"Terrain_{cell.x}_{cell.y}_{i}",
                    new Vector3(x, height * .5f, z),
                    new Vector3(width, height, width),
                    Color.Lerp(dungeonFloorColor, dungeonAccentColor, .5f));
            }
        }

        if (shape == "Blood" || shape == "Fairy" || shape == "Puzzle" || shape == "Trap")
        {
            Vector2Int cell = cells[0];
            Color marker = Color.Lerp(dungeonAccentColor, mapColor, .6f);
            CreateBlock(parent, $"Landmark_{shape}",
                new Vector3(cell.x * spacing, .35f, cell.y * spacing),
                new Vector3(size * .15f, .7f, size * .15f), marker);
        }
    }

    private void CreateDungeonWall(Transform parent, Vector2Int cell,
        Vector2Int direction, float size, float thickness, float height, float opening)
    {
        float cx = cell.x * WorldSpacing;
        float cz = cell.y * WorldSpacing;
        bool onXSide = direction.x != 0;
        float wallX = cx + direction.x * size * .5f;
        float wallZ = cz + direction.y * size * .5f;
        float segmentLength = opening > 0f ? (size - opening) * .5f : size;
        if (segmentLength <= .01f) return;

        int segments = opening > 0f ? 2 : 1;
        for (int i = 0; i < segments; i++)
        {
            float offset = opening > 0f
                ? (i == 0 ? -1f : 1f) * (opening + segmentLength) * .5f : 0f;
            Vector3 position = onXSide
                ? new Vector3(wallX, height * .5f, cz + offset)
                : new Vector3(cx + offset, height * .5f, wallZ);
            Vector3 scale = onXSide
                ? new Vector3(thickness, height, segmentLength)
                : new Vector3(segmentLength, height, thickness);
            CreateBlock(parent, $"Wall_{cell.x}_{cell.y}_{direction.x}_{direction.y}_{i}",
                position, scale, dungeonWallColor);
        }
    }

    // Fill each internal adjacency once, including the central square of 2x2
    // rooms. All bridges stay within their own grid footprint, unlike the
    // old neighbor-dependent tile scaling.
    private void JoinRoomCells(Transform parent, List<Vector2Int> cells,
        float spacing, float size, float height, float yCenter, Color color)
    {
        HashSet<Vector2Int> members = new(cells);
        float gap = spacing - size;
        if (gap <= .001f) return;
        foreach (Vector2Int cell in cells)
        {
            if (members.Contains(cell + Vector2Int.right))
                CreateBlock(parent, $"InternalBridge_X_{cell.x}_{cell.y}",
                    new Vector3((cell.x + .5f) * spacing, yCenter,
                        cell.y * spacing),
                    new Vector3(gap + .02f, height, size), color);

            if (members.Contains(cell + Vector2Int.up))
                CreateBlock(parent, $"InternalBridge_Z_{cell.x}_{cell.y}",
                    new Vector3(cell.x * spacing, yCenter,
                        (cell.y + .5f) * spacing),
                    new Vector3(size, height, gap + .02f), color);

            if (members.Contains(cell + Vector2Int.right) &&
                members.Contains(cell + Vector2Int.up) &&
                members.Contains(cell + Vector2Int.one))
                CreateBlock(parent, $"InternalCorner_{cell.x}_{cell.y}",
                    new Vector3((cell.x + .5f) * spacing, yCenter,
                        (cell.y + .5f) * spacing),
                    new Vector3(gap + .02f, height, gap + .02f), color);
        }
    }

    private void CreateCorridor(Vector2Int a, Vector2Int b, Color color)
    {
        GameObject parent = new($"Corridor_{a.x}_{a.y}_to_{b.x}_{b.y}");
        parent.transform.SetParent(transform, false);
        generated.Add(parent);

        float spacing = WorldSpacing;
        Vector3 middle = new((a.x + b.x) * spacing * .5f, 0f,
            (a.y + b.y) * spacing * .5f);
        bool alongX = a.x != b.x;

        if (generationMode == DungeonGenerationMode.DungeonMap)
        {
            CreateBlock(parent.transform, "MapPassage", middle,
                alongX
                    ? new Vector3(spacing, .3f, corridorWidth)
                    : new Vector3(corridorWidth, .3f, spacing), color);
            return;
        }

        float length = spacing - DungeonSpan + .04f;
        float width = DungeonPassageWidth;
        float floorThickness = .65f;
        float wallHeight = Mathf.Max(1f, dungeonWallHeight);
        float thickness = Mathf.Clamp(dungeonWallThickness, .1f, DungeonSpan * .15f);

        CreateBlock(parent.transform, "PassageFloor",
            middle + Vector3.down * floorThickness * .5f,
            alongX
                ? new Vector3(length, floorThickness, width)
                : new Vector3(width, floorThickness, length), color);

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 position = alongX
                ? middle + new Vector3(0f, wallHeight * .5f,
                    side * (width + thickness) * .5f)
                : middle + new Vector3(side * (width + thickness) * .5f,
                    wallHeight * .5f, 0f);
            Vector3 dimensions = alongX
                ? new Vector3(length, wallHeight, thickness)
                : new Vector3(thickness, wallHeight, length);
            CreateBlock(parent.transform, side == -1 ? "LeftWall" : "RightWall",
                position, dimensions, dungeonWallColor);
        }

        if (generateCeilings)
            CreateBlock(parent.transform, "PassageCeiling",
                middle + Vector3.up * (wallHeight + thickness * .5f),
                alongX
                    ? new Vector3(length, thickness, width)
                    : new Vector3(width, thickness, length), dungeonWallColor);
    }

    private GameObject CreateBlock(Transform parent, string name,
        Vector3 position, Vector3 scale, Color color)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = position;
        block.transform.localScale = scale;
        SetColor(block, color);
        return block;
    }

    private void FrameMainCamera(int width, int height)
    {
        if (!frameMainCamera || Camera.main == null) return;
        Camera camera = Camera.main;
        float spacing = WorldSpacing;
        Vector3 center = new((width - 1) * spacing * .5f,
            0f, (height - 1) * spacing * .5f);

        if (generationMode == DungeonGenerationMode.DungeonMap)
        {
            camera.orthographic = true;
            camera.transform.position = center +
                Vector3.up * Mathf.Max(width, height) * spacing * 1.5f;
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            float verticalHalf = height * spacing * .5f;
            float horizontalHalf = width * spacing /
                (2f * Mathf.Max(.1f, camera.aspect));
            camera.orthographicSize = Mathf.Max(verticalHalf, horizontalHalf) + spacing;
        }
        else
        {
            camera.orthographic = false;
            camera.fieldOfView = 55f;
            float distance = Mathf.Max(width, height) * spacing * 1.2f;
            camera.transform.position = center +
                new Vector3(distance * .65f, distance * .9f, -distance * .7f);
            camera.transform.LookAt(center);
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, distance * 4f);
        }
    }

    private void SetColor(GameObject obj, Color color)
    {
        Color32 value = color;
        uint key = ((uint)value.r << 24) | ((uint)value.g << 16) |
            ((uint)value.b << 8) | value.a;
        if (!generatedMaterials.TryGetValue(key, out Material material))
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return;
            material = new Material(shader) { color = color };
            generatedMaterials.Add(key, material);
        }
        obj.GetComponent<Renderer>().sharedMaterial = material;
    }
}
