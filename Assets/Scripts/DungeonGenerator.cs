using UnityEngine;
using System.Collections.Generic;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Dungeon Settings")]
    public int gridSize = 6;
    public int roomCount = 20;
    public float roomSpacing = 5f;
    public int seed = 12345;

    [Header("Visual Settings")]
    public float roomSize = 4f;
    public float corridorWidth = 1.5f;

    private readonly HashSet<Vector2Int> rooms = new();
    private readonly HashSet<(Vector2Int, Vector2Int)> connections = new();

    private static readonly Vector2Int[] directions =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private Vector2Int startRoom;
    private Vector2Int bossRoom;

    void Start()
    {
        Generate();
    }

    public void Generate()
    {
        // 前回の生成結果を削除
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        rooms.Clear();
        connections.Clear();

        Random.InitState(seed);

        int size = Mathf.Max(1, gridSize);
        int targetCount = Mathf.Clamp(roomCount, 1, size * size);

        startRoom = new Vector2Int(size / 2, size / 2);

        rooms.Add(startRoom);

        Vector2Int current = startRoom;

        int attempts = 0;

        // ランダムウォーク
        while (rooms.Count < targetCount && attempts < 10000)
        {
            attempts++;

            Vector2Int direction = directions[Random.Range(0, 4)];
            Vector2Int next = current + direction;

            if (next.x < 0 || next.y < 0 ||
                next.x >= size || next.y >= size)
                continue;

            // 接続を記録
            AddConnection(current, next);

            current = next;
            rooms.Add(current);
        }

        // STARTから最も遠い部屋をBOSSにする
        bossRoom = FindFarthestRoom();

        // 部屋を生成
        foreach (Vector2Int position in rooms)
        {
            CreateRoom(position);
        }

        // 通路を生成
        foreach (var connection in connections)
        {
            CreateCorridor(connection.Item1, connection.Item2);
        }

        Debug.Log(
            $"Generated {rooms.Count} rooms, " +
            $"{connections.Count} connections. Seed: {seed}"
        );
    }

    void AddConnection(Vector2Int a, Vector2Int b)
    {
        // 接続の重複を防ぐ
        if (a.x > b.x || (a.x == b.x && a.y > b.y))
        {
            (a, b) = (b, a);
        }

        connections.Add((a, b));
    }

    Vector2Int FindFarthestRoom()
    {
        // 接続関係に沿って最短距離を計算
        Queue<Vector2Int> queue = new();
        Dictionary<Vector2Int, int> distances = new();

        queue.Enqueue(startRoom);
        distances[startRoom] = 0;

        Vector2Int farthest = startRoom;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = current + direction;

                if (!rooms.Contains(next) || distances.ContainsKey(next))
                    continue;

                if (!HasConnection(current, next))
                    continue;

                distances[next] = distances[current] + 1;
                queue.Enqueue(next);

                if (distances[next] > distances[farthest])
                    farthest = next;
            }
        }

        return farthest;
    }

    bool HasConnection(Vector2Int a, Vector2Int b)
    {
        if (a.x > b.x || (a.x == b.x && a.y > b.y))
        {
            (a, b) = (b, a);
        }

        return connections.Contains((a, b));
    }

    void CreateRoom(Vector2Int position)
    {
        GameObject room = GameObject.CreatePrimitive(PrimitiveType.Cube);

        room.name = $"Room_{position.x}_{position.y}";
        room.transform.SetParent(transform);

        room.transform.localPosition = new Vector3(
            position.x * roomSpacing,
            0,
            position.y * roomSpacing
        );

        room.transform.localScale = new Vector3(
            roomSize,
            0.5f,
            roomSize
        );

        Color color = Color.white;

        if (position == startRoom)
        {
            color = Color.green;
            room.name = "START";
        }
        else if (position == bossRoom)
        {
            color = Color.red;
            room.name = "BOSS";
        }

        SetColor(room, color);
    }

    void CreateCorridor(Vector2Int a, Vector2Int b)
    {
        Vector3 posA = new Vector3(
            a.x * roomSpacing, 0, a.y * roomSpacing
        );

        Vector3 posB = new Vector3(
            b.x * roomSpacing, 0, b.y * roomSpacing
        );

        GameObject corridor =
            GameObject.CreatePrimitive(PrimitiveType.Cube);

        corridor.name = $"Corridor_{a}_{b}";
        corridor.transform.SetParent(transform);

        corridor.transform.localPosition = (posA + posB) / 2f;

        Vector3 difference = posB - posA;

        if (Mathf.Abs(difference.x) > 0)
        {
            corridor.transform.localScale = new Vector3(
                roomSpacing,
                0.3f,
                corridorWidth
            );
        }
        else
        {
            corridor.transform.localScale = new Vector3(
                corridorWidth,
                0.3f,
                roomSpacing
            );
        }

        SetColor(corridor, Color.gray);
    }

    void SetColor(GameObject obj, Color color)
    {
        Renderer renderer = obj.GetComponent<Renderer>();

        Material material = new Material(
            Shader.Find("Universal Render Pipeline/Lit")
        );

        material.color = color;
        renderer.material = material;
    }
}