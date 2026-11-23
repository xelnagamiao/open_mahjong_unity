using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class GameCanvas {
    private const float SbrDiceSeconds = SbrPacketDownSeconds + 1.50f;
    private const float SbrDiceRollSeconds = .90f;

    private IEnumerator AnimateSbrDice(AskHandActionGBInfo info) {
        var phase = info.xueliu_exchange;
        var rolling = AnimateTableExchangeDice(phase.dice, phase.animation_seconds,
            NormalGameStateManager.Instance?.indexToPosition, 3, info.player_index, sbrOutgoingTiles);
        while (rolling.MoveNext()) yield return rolling.Current;
        exchangeFlight = null;
    }

    private IEnumerator AnimateTableExchangeDice(int[] dice, float remaining,
            IReadOnlyDictionary<int, string> positions, int tileCount, int localPlayer, int[] localOutgoing, System.Action<float> poseWorldDice = null) {
        if (dice == null || dice.Length != 2 || dice[0] < 1 || dice[0] > 6 || dice[1] < 1 || dice[1] > 6) {
            yield return null;
            yield break;
        }
        var tray = SbrNode(exchangePresentationRoot, "SbrExchangeDice", new Vector2(747, 420));
        var packets = CreateTablePackets(GetTableExchangeRoutes(positions, SbrSeats, SbrSeats), positions,
            tray, tileCount, localPlayer, localOutgoing);
        var red = poseWorldDice == null ? CreateSbrDie(tray, "SbrRedDie", dice[0], true) : null;
        var white = poseWorldDice == null ? CreateSbrDie(tray, "SbrWhiteDie", dice[1], false) : null;
        void PoseDice(float roll) {
            if (poseWorldDice != null) poseWorldDice(roll);
            else { red.Pose(roll); white.Pose(roll); }
        }
        float elapsed = Mathf.Clamp(SbrDiceSeconds - remaining, 0f, SbrDiceSeconds);
        do {
            if (packets != null) packets.Place(SbrRollToBack(elapsed), 0);
            else PlaceSbrReference(tray, exchangePresentationRoot);
            bool showDice = elapsed >= SbrPacketDownSeconds;
            if (tray.gameObject.activeSelf != showDice) tray.gameObject.SetActive(showDice);
            if (showDice) {
                float roll = Mathf.Clamp01((elapsed - SbrPacketDownSeconds) / SbrDiceRollSeconds);
                PoseDice(roll);
            }
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        } while (elapsed < SbrDiceSeconds);
        tray.gameObject.SetActive(true);
        packets?.Place(1, 0);
        PoseDice(1f);
    }

    private static SbrExchangeDie CreateSbrDie(RectTransform parent, string name, int face, bool red) {
        var die = SbrNode(parent, name, new Vector2(70f, 78f)).gameObject.AddComponent<SbrExchangeDie>();
        die.raycastTarget = false;
        die.SetResult(face, red);
        return die;
    }
}

internal sealed class SbrExchangeDie : MaskableGraphic {
    private const float Bevel = .86f;
    private const float HalfEdge = 13.5f;
    private static readonly Vector3 Viewer = new Vector3(0f, .86f, -.51f).normalized;
    private static readonly Vector3 ScreenUp = new Vector3(0f, -Viewer.z, Viewer.y);
    private static readonly Vector3 Light = new Vector3(-.38f, .9f, -.21f).normalized;
    private static readonly Vector3[] Normals = {
        Vector3.zero, Vector3.up, Vector3.forward, Vector3.right,
        Vector3.left, Vector3.back, Vector3.down
    };
    private static readonly int[][] PipSlots = {
        System.Array.Empty<int>(), new[] {4}, new[] {0, 8}, new[] {0, 4, 8},
        new[] {0, 2, 6, 8}, new[] {0, 2, 4, 6, 8}, new[] {0, 2, 3, 5, 6, 8}
    };
    private static readonly Vector2[] Circle = MakeCircle();
    private static readonly Color RedBody = new Color(.83f, .17f, .16f);
    private static readonly Color IvoryBody = new Color(.98f, .95f, .84f);
    private static readonly Color Ink = new Color(.13f, .12f, .14f);
    private static readonly Color Cinnabar = new Color(.77f, .12f, .10f);
    private static readonly Color IvoryPip = new Color(1f, .97f, .87f);
    private bool isRed;
    private Quaternion settled;
    private float lift;
    public int Result { get; private set; }
    public Quaternion Rotation { get; private set; }
    public int TopFace {
        get {
            int top = 1;
            for (int face = 2; face <= 6; face++)
                if ((Rotation * Normals[face]).y > (Rotation * Normals[top]).y) top = face;
            return top;
        }
    }

    internal void SetResult(int result, bool red) {
        Result = result;
        isRed = red;
        settled = Quaternion.AngleAxis(red ? -12f : 16f, Vector3.up) *
                  Quaternion.FromToRotation(Normals[result], Vector3.up);
    }

    internal void Pose(float roll) {
        if (roll >= 1f && Rotation.Equals(settled)) return;
        float remaining = 1f - roll;
        float drag = remaining * remaining;
        Vector2 stop = isRed ? new Vector2(-23f, -67f) : new Vector2(22f, -70f);
        Vector2 run = isRed ? new Vector2(-54f, 13f) : new Vector2(46f, 20f);
        rectTransform.anchoredPosition = stop + run * drag;
        lift = remaining * Mathf.Abs(Mathf.Sin(roll * Mathf.PI * 3f)) * (isRed ? 13f : 10f);
        var spin = isRed ? new Vector3(535f, -286f, 161f) : new Vector3(-401f, 384f, -237f);
        Rotation = Quaternion.Euler(spin * drag) * settled;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh) {
        mesh.Clear();
        if (Result == 0) return;
        Ellipse(mesh, new Vector2(2f, -13f), new Vector2(18f, 0), new Vector2(0, 6f), new Color(0, 0, 0, .13f));
        Ellipse(mesh, new Vector2(2f, -13f), new Vector2(14f, 0), new Vector2(0, 4f), new Color(0, 0, 0, .17f));
        for (int face = 1; face <= 6; face++) {
            Vector3 normal = Normals[face];
            if (!Visible(normal)) continue;
            Vector3 u = Mathf.Abs(normal.y) > .5f ? Vector3.right : Vector3.up;
            Vector3 v = Vector3.Cross(normal, u);
            Quad(mesh, normal - Bevel * (u + v), normal + Bevel * (u - v),
                normal + Bevel * (u + v), normal + Bevel * (v - u), Shade(normal));
            Color pipColor = isRed ? IvoryPip : (face == 1 || face == 4 ? Cinnabar : Ink);
            float radius = face == 1 ? .25f : .144f;
            Vector2 x = ProjectVector(u) * radius, y = ProjectVector(v) * radius;
            foreach (int index in PipSlots[face]) {
                Vector3 point = normal + .47f * ((index % 3 - 1) * u + (1 - index / 3) * v);
                Ellipse(mesh, Project(point), x, y, pipColor);
            }
        }
        for (int axis = 0; axis < 3; axis++) {
            Vector3 a = Vector3.zero, b = Vector3.zero, c = Vector3.zero;
            a[axis] = 1f;
            b[(axis + 1) % 3] = 1f;
            c[(axis + 2) % 3] = 1f;
            for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2) {
                Vector3 n = a * i, m = b * j;
                if (!Visible(n + m)) continue;
                Vector3 e = n + Bevel * m, f = m + Bevel * n, end = Bevel * c;
                Quad(mesh, e - end, f - end, f + end, e + end, Shade((n + m).normalized));
            }
        }
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2) {
            var n = new Vector3(x, y, z);
            if (!Visible(n)) continue;
            Color tint = Shade(n.normalized);
            int index = mesh.currentVertCount;
            mesh.AddVert(Project(new Vector3(x, y * Bevel, z * Bevel)), tint, Vector2.zero);
            mesh.AddVert(Project(new Vector3(x * Bevel, y, z * Bevel)), tint, Vector2.zero);
            mesh.AddVert(Project(new Vector3(x * Bevel, y * Bevel, z)), tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
        }
    }

    private bool Visible(Vector3 n) => Vector3.Dot(Rotation * n, Viewer) > .001f;

    private Color Shade(Vector3 n) {
        Color body = isRed ? RedBody : IvoryBody;
        float light = .80f + .22f * Vector3.Dot(Rotation * n, Light);
        return new Color(body.r * light, body.g * light, body.b * light);
    }

    private Vector2 ProjectVector(Vector3 p) {
        Vector3 w = Rotation * p;
        return new Vector2(w.x, Vector3.Dot(w, ScreenUp)) * HalfEdge;
    }

    private Vector2 Project(Vector3 p) => ProjectVector(p) + Vector2.up * lift;

    private void Quad(VertexHelper mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color tint) {
        int start = mesh.currentVertCount;
        mesh.AddVert(Project(a), tint, Vector2.zero);
        mesh.AddVert(Project(b), tint, Vector2.zero);
        mesh.AddVert(Project(c), tint, Vector2.zero);
        mesh.AddVert(Project(d), tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }

    private static void Ellipse(VertexHelper mesh, Vector2 center, Vector2 x, Vector2 y, Color tint) {
        int start = mesh.currentVertCount;
        mesh.AddVert(center, tint, Vector2.zero);
        foreach (Vector2 point in Circle)
            mesh.AddVert(center + point.x * x + point.y * y, tint, Vector2.zero);
        for (int i = 0; i < Circle.Length; i++)
            mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % Circle.Length);
    }

    private static Vector2[] MakeCircle() {
        var points = new Vector2[16];
        for (int i = 0; i < points.Length; i++) {
            float angle = i * (2f * Mathf.PI / points.Length);
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
        return points;
    }
}
