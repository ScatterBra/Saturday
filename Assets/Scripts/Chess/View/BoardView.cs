using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 棋盘的显示：64 个格子 + 所有棋子的 PieceView。棋盘平放在根物体的 XZ 平面上，
    /// 格子 (x, y) 的中心在本地坐标 (x - 3.5, 0, y - 3.5)。
    /// 第三阶段的高原用一格一格升起的方块表示，每级高 LevelHeight；城堡格是灰色石块。
    /// 场景里摆了棋盘图（ChessDesk.board）时：平地格子藏在棋盘图下面，高亮、岩浆改用盖在棋盘图上的半透明色块。
    /// </summary>
    public sealed class BoardView
    {
        /// <summary>高原每升一级在画面上抬高多少（世界单位，一格 = 1）。</summary>
        public const float LevelHeight = 0.5f;

        static readonly Color Light = new Color(0.93f, 0.87f, 0.74f);
        static readonly Color Dark = new Color(0.72f, 0.56f, 0.4f);
        static readonly Color Lava = new Color(1f, 0.32f, 0.05f);
        static readonly Color Rock = new Color(0.55f, 0.5f, 0.42f);
        static readonly Color CastleStone = new Color(0.62f, 0.62f, 0.66f);
        static readonly Color Selected = new Color(1f, 0.9f, 0.3f);
        static readonly Color MoveTarget = new Color(0.55f, 0.85f, 0.45f);
        static readonly Color CaptureTarget = new Color(0.95f, 0.4f, 0.35f);
        static readonly Color PickTarget = new Color(0.35f, 0.8f, 0.95f);
        static readonly Color LastMove = new Color(0.95f, 0.8f, 0.5f);
        static readonly Color Danger = new Color(0.9f, 0.1f, 0.1f);
        static readonly Color EffectTarget = new Color(0.75f, 0.45f, 1f);
        /// <summary>技能预览：会打到敌人的格子、会打到自己人的格子、空格。</summary>
        static readonly Color AffectEnemy = new Color(1f, 0.25f, 0.2f);
        static readonly Color AffectOwn = new Color(1f, 0.85f, 0.1f);
        static readonly Color AffectEmpty = new Color(1f, 0.6f, 0.5f);
        /// <summary>王后预告要打的格子。</summary>
        static readonly Color Warning = new Color(0.65f, 0.3f, 0.95f);
        /// <summary>城门开了以后的城堡格。</summary>
        static readonly Color GateOpenColor = new Color(0.45f, 0.95f, 0.5f);

        readonly Transform root;
        readonly ChessViewFactory factory;
        readonly GameObject[] squares = new GameObject[64];
        /// <summary>升起的地块用贴图方块叠出来时，最上面那块方块的渲染器（顶面上色用）；平地和城堡为 null。</summary>
        readonly MeshRenderer[] terrainTops = new MeshRenderer[64];
        readonly List<GameObject> terrainBlocks = new List<GameObject>();
        readonly Texture2D[] raisedSideTextures;
        readonly Texture2D raisedSkullTexture;
        readonly float raisedSkullChance;
        Mesh blockMesh;
        Material blockTopMaterial;
        Material[] blockSideMaterials;
        Material blockSkullMaterial;
        readonly MaterialPropertyBlock topBlock = new MaterialPropertyBlock();
        /// <summary>有棋盘图时盖在每个格子上的色块（高亮、岩浆、暗化），没有棋盘图时为 null。</summary>
        readonly SpriteRenderer[] overlays;
        readonly bool hasBoardArt;
        readonly Dictionary<int, PieceView> pieces = new Dictionary<int, PieceView>();
        readonly Transform pieceRoot;
        GameObject castleLabel;
        Board board;
        readonly GameObject lavaEffect;
        readonly float lavaEffectScale;
        readonly float lavaEffectLift;
        readonly List<GameObject> lavaEffects = new List<GameObject>();
        readonly GameObject stormCloud;
        readonly float stormCloudClearance, stormCloudSize, stormCloudPuffScale, stormCloudOpacity, stormCloudDensity;
        GameObject stormCloudInstance;
        /// <summary>调淡了的云材质（每次生成一份副本，云删掉时一起删）。</summary>
        Material stormCloudMaterial;
        readonly Material dissolveMaterial;
        readonly float dissolveDuration;
        readonly GameObject dissolveParticles;
        readonly float dissolveParticlesScale;
        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int NoiseOffsetId = Shader.PropertyToID("_NoiseOffset");
        readonly Sprite[] hydrantSprayFrames;
        readonly float hydrantSprayFps, hydrantSprayHeight, hydrantSprayOffset;
        readonly GameObject treeEffect;
        readonly float treeEffectScale;
        readonly GameObject explosionEffect, rainEffect, thunderEffect;
        readonly float explosionEffectScale = 0.5f, rainEffectScale = 1f, rainDuration = 2.5f, thunderEffectScale = 0.5f;
        readonly Color beamColor = new Color(2.5f, 2.2f, 0.9f);
        /// <summary>一直标着的格子（王后预告要打的格子），每次 Highlight 都画上。</summary>
        readonly List<Coord> warnings = new List<Coord>();
        /// <summary>每枚棋子现在是不是显示成树；变了就播一次树特效。</summary>
        readonly Dictionary<int, bool> treeShown = new Dictionary<int, bool>();
        /// <summary>换新棋盘时只记下谁是树，不播特效。</summary>
        bool quietTrees;
        /// <summary>上次 ConsumeTreeVanished 之后有树消失过。</summary>
        bool treeVanished;

        /// <summary>
        /// 自上次调用以来有没有树消失（变回兵、被吃、掉进岩浆），读完清零。
        /// 好几棵树同时变回兵也只算一次，音效只播一遍。
        /// </summary>
        public bool ConsumeTreeVanished()
        {
            bool vanished = treeVanished;
            treeVanished = false;
            return vanished;
        }

        public BoardView(Transform parent, ChessViewFactory factory, ChessDesk desk = null)
        {
            this.factory = factory;
            root = new GameObject("Board").transform;
            root.SetParent(parent, false);
            hasBoardArt = desk != null && desk.HasBoardArt;
            if (desk != null)
            {
                lavaEffect = desk.lavaEffect;
                lavaEffectScale = desk.lavaEffectScale;
                lavaEffectLift = desk.lavaEffectLift;
                treeEffect = desk.treeEffect;
                raisedSideTextures = desk.raisedSideTextures;
                raisedSkullTexture = desk.raisedSkullTexture;
                raisedSkullChance = desk.raisedSkullChance;
                hydrantSprayFrames = desk.hydrantSprayFrames;
                hydrantSprayFps = desk.hydrantSprayFps;
                hydrantSprayHeight = desk.hydrantSprayHeight;
                hydrantSprayOffset = desk.hydrantSprayOffset;
                dissolveMaterial = desk.dissolveMaterial;
                dissolveDuration = desk.dissolveDuration;
                dissolveParticles = desk.dissolveParticles;
                dissolveParticlesScale = desk.dissolveParticlesScale;
                stormCloud = desk.stormCloud;
                stormCloudClearance = desk.stormCloudClearance;
                stormCloudSize = desk.stormCloudSize;
                stormCloudPuffScale = desk.stormCloudPuffScale;
                stormCloudOpacity = desk.stormCloudOpacity;
                stormCloudDensity = desk.stormCloudDensity;
                treeEffectScale = desk.treeEffectScale;
                explosionEffect = desk.explosionEffect;
                explosionEffectScale = desk.explosionEffectScale;
                rainEffect = desk.rainEffect;
                rainEffectScale = desk.rainEffectScale;
                rainDuration = desk.rainDuration;
                thunderEffect = desk.thunderEffect;
                thunderEffectScale = desk.thunderEffectScale;
                beamColor = desk.beamColor;
                factory.WetTint = desk.wetTint;
            }

            // 桌面（backdrop）：顶面在 y = -0.405，在 0.4 厚的棋盘模型底下，比相机能看到的范围大很多，宽屏也不会露边
            var tableTop = factory.Primitive(PrimitiveType.Cube, root, new Vector3(0, -0.455f, 0), new Vector3(80f, 0.1f, 50f), surface: true);
            tableTop.name = "Table Top";
            factory.SetColor(tableTop, new Color(0.42f, 0.3f, 0.22f));
            var frame = factory.Primitive(PrimitiveType.Cube, root, new Vector3(0, -0.12f, 0), new Vector3(9f, 0.1f, 9f), surface: true);
            factory.SetColor(frame, new Color(0.3f, 0.2f, 0.14f));
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                squares[i] = factory.Primitive(PrimitiveType.Cube, root, SquareCenter(c) + new Vector3(0, -0.05f, 0), new Vector3(1f, 0.1f, 1f), surface: true);
                squares[i].name = c.ToString();
            }
            bool coordinatesInArt = hasBoardArt && desk.boardHasCoordinates;
            for (int i = 0; i < 8 && !coordinatesInArt; i++)
            {
                var file = factory.Label(root, new Vector3(i - 3.5f, 0.01f, -4.25f), 0.035f, ((char)('a' + i)).ToString());
                file.color = new Color(0.95f, 0.9f, 0.8f);
                var rank = factory.Label(root, new Vector3(-4.25f, 0.01f, i - 3.5f), 0.035f, (i + 1).ToString());
                rank.color = new Color(0.95f, 0.9f, 0.8f);
            }

            if (hasBoardArt)
            {
                frame.SetActive(false);
                overlays = new SpriteRenderer[64];
                var white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                    new Vector2(0.5f, 0.5f), Texture2D.whiteTexture.width);
                for (int i = 0; i < 64; i++)
                {
                    overlays[i] = FlatSprite($"Overlay {Coord.FromIndex(i)}", white, SquareCenter(Coord.FromIndex(i)) + Vector3.up * 0.004f, -10);
                    overlays[i].enabled = false;
                }
            }
            pieceRoot = new GameObject("Pieces").transform;
            pieceRoot.SetParent(root, false);
        }

        public static Vector3 SquareCenter(Coord c) => new Vector3(c.X - 3.5f, 0f, c.Y - 3.5f);

        /// <summary>平躺在棋盘上、面朝上的贴图。</summary>
        SpriteRenderer FlatSprite(string name, Sprite sprite, Vector3 localPosition, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        /// <summary>棋子站在这个格子上时的位置（考虑高原高度）。</summary>
        public Vector3 PiecePosition(Coord c) => SquareCenter(c) + Vector3.up * TopOf(c);

        float TopOf(Coord c) => board != null ? board.Height(c) * LevelHeight : 0f;

        /// <summary>屏幕射线落在哪个格子上（升起的格子按它的顶面算）。</summary>
        public bool TryGetCoord(Ray ray, out Coord coord)
        {
            coord = default;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                var top = root.TransformPoint(SquareCenter(c) + Vector3.up * TopOf(c));
                var plane = new Plane(root.up, top);
                if (!plane.Raycast(ray, out float distance) || distance >= best) continue;
                var local = root.InverseTransformPoint(ray.GetPoint(distance)) - SquareCenter(c);
                if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.z) > 0.5f) continue;
                best = distance;
                coord = c;
                found = true;
            }
            return found;
        }

        /// <summary>鼠标射线碰到的、离相机最近的棋子（复活预览的影子不算）。</summary>
        public bool TryGetPiece(Ray ray, out int pieceId)
        {
            pieceId = -1;
            float best = float.MaxValue;
            foreach (var pair in pieces)
            {
                if (board == null || !board.GetPiece(pair.Key).OnBoard) continue;
                if (!pair.Value.Raycast(ray, out float distance) || distance >= best) continue;
                best = distance;
                pieceId = pair.Key;
            }
            return pieceId >= 0;
        }

        /// <summary>鼠标指着哪一格：先看有没有点中棋子本身（立起来的棋子会挡住后面的格子），没有再按格子顶面算。</summary>
        public bool TryGetPointedCoord(Ray ray, out Coord coord)
        {
            if (TryGetPiece(ray, out int id))
            {
                coord = board.GetPiece(id).Pos;
                return true;
            }
            return TryGetCoord(ray, out coord);
        }

        /// <summary>换一局新棋：清掉旧棋子，按新棋盘重建。</summary>
        public void Bind(Board newBoard)
        {
            HideGhost();
            foreach (var view in pieces.Values) view.Destroy();
            pieces.Clear();
            treeShown.Clear();
            treeVanished = false;
            board = newBoard;
            RebuildTerrain();
            quietTrees = true;
            try { SyncAll(); }
            finally { quietTrees = false; }
            Highlight(null, null, null, null);
        }

        /// <summary>按棋盘的高度和城堡重新摆格子（第三阶段风暴之后调用）。</summary>
        public void RebuildTerrain()
        {
            Vector3 castleSum = Vector3.zero;
            int castleCount = 0;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                float top = TopOf(c);
                float thickness = 0.1f + top;
                squares[i].transform.localPosition = SquareCenter(c) + Vector3.up * (top - thickness / 2);
                squares[i].transform.localScale = new Vector3(1f, thickness, 1f);
                if (overlays != null) overlays[i].transform.localPosition = SquareCenter(c) + Vector3.up * (top + 0.004f);
                if (board != null && board.IsCastle(c))
                {
                    castleSum += SquareCenter(c) + Vector3.up * top;
                    castleCount++;
                }
            }

            RebuildTerrainBlocks();
            RebuildLavaEffects();

            if (castleLabel != null) ViewUtil.Destroy(castleLabel);
            castleLabel = null;
            if (castleCount > 0)
            {
                var label = factory.Label(root, castleSum / castleCount + Vector3.up * 0.8f, 0.05f, "Castle", title: true);
                label.color = new Color(1f, 0.85f, 0.95f);
                castleLabel = label.gameObject;
                RefreshCastleLabel();
            }

            RebuildStormCloud();
        }

        /// <summary>
        /// 第三阶段在棋盘上空放一团雷雨云（ChessDesk.stormCloud）：铺满整个棋盘，比城堡和 Princess 字样高，比相机低。
        /// 原特效只有 2.4 × 1 格大，这里把发射范围拉到 stormCloudSize 见方，粒子放大、数量按面积加。
        /// </summary>
        void RebuildStormCloud()
        {
            if (stormCloudInstance != null) ViewUtil.Destroy(stormCloudInstance);
            if (stormCloudMaterial != null) ViewUtil.Destroy(stormCloudMaterial);
            stormCloudInstance = null;
            stormCloudMaterial = null;
            if (stormCloud == null || board == null || !board.Stage3) return;

            float highest = 0f;
            for (int i = 0; i < 64; i++) highest = Mathf.Max(highest, TopOf(Coord.FromIndex(i)));
            if (castleLabel != null) highest = Mathf.Max(highest, castleLabel.transform.localPosition.y + 0.3f);

            var fx = ParticleFx.Spawn(stormCloud, root, new Vector3(0f, highest + stormCloudClearance, 0f), 1f);
            fx.name = "Storm Cloud";
            float puff = Mathf.Max(0.1f, stormCloudPuffScale);
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var shape = ps.shape;
                if (!shape.enabled) continue;
                var main = ps.main;
                var emission = ps.emission;
                if (shape.shapeType == ParticleSystemShapeType.Box)
                {
                    // 云本身：盒子在 prefab 里是横躺的（本地 X、Y 是水平方向），铺到整个棋盘
                    float oldArea = Mathf.Max(0.01f, shape.scale.x * shape.scale.y);
                    shape.scale = new Vector3(stormCloudSize, stormCloudSize, shape.scale.z);
                    float more = stormCloudSize * stormCloudSize / oldArea / (puff * puff) * stormCloudDensity;
                    emission.rateOverTimeMultiplier *= more;
                    main.maxParticles = Mathf.Max(main.maxParticles, Mathf.CeilToInt(main.maxParticles * more));
                    main.startSizeMultiplier *= puff;
                    // 云挡在相机和棋盘之间：把材质调淡，透出下面的棋子
                    var renderer = ps.GetComponent<ParticleSystemRenderer>();
                    if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColor"))
                    {
                        stormCloudMaterial = new Material(renderer.sharedMaterial);
                        var tint = stormCloudMaterial.GetColor("_BaseColor");
                        tint.a *= stormCloudOpacity;
                        stormCloudMaterial.SetColor("_BaseColor", tint);
                        renderer.sharedMaterial = stormCloudMaterial;
                    }
                }
                else
                {
                    // 闪电的灯光、雨：发射范围也跟着铺开
                    float oldRadius = Mathf.Max(0.01f, shape.radius);
                    shape.radius = stormCloudSize * 0.45f;
                    float more = Mathf.Pow(shape.radius / oldRadius, 2f);
                    emission.rateOverTimeMultiplier *= Mathf.Min(more, 10f);
                    main.maxParticles = Mathf.Max(main.maxParticles, Mathf.CeilToInt(main.maxParticles * Mathf.Min(more, 10f)));
                }
            }
            // 风暴散去时云已经聚好了
            ParticleFx.Warm(fx, 3f);
            stormCloudInstance = fx;
        }

        /// <summary>
        /// 升起的地块（城堡除外）换成贴图方块：每块 1 × 1 × 1，最上面一块的顶边和地块顶面对齐，
        /// 侧面超过 1 格高就往下再叠一块，直到盖住棋盘面。普通方块四面轮流贴 raisedSideTextures；
        /// 少数是骷髅方块，其中一面贴 raisedSkullTexture。每块随机转 90° 的倍数，看起来不会一模一样。
        /// 顶面还是纯色（Highlight 给它上色），以后有顶面贴图再换。
        /// </summary>
        void RebuildTerrainBlocks()
        {
            foreach (var block in terrainBlocks) ViewUtil.Destroy(block);
            terrainBlocks.Clear();
            for (int i = 0; i < 64; i++) terrainTops[i] = null;
            bool textured = raisedSideTextures != null && raisedSideTextures.Length > 0 && raisedSideTextures[0] != null;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                bool raised = board != null && board.Height(c) > 0 && !board.IsCastle(c);
                squares[i].SetActive(!(textured && raised));
                if (!textured || !raised) continue;
                EnsureBlockAssets(squares[i].GetComponent<MeshRenderer>().sharedMaterial);

                float top = TopOf(c);
                int count = Mathf.CeilToInt(top - 0.001f);
                for (int layer = 0; layer < count; layer++)
                {
                    var go = new GameObject($"Terrain {c} {layer}");
                    go.transform.SetParent(root, false);
                    go.transform.localPosition = SquareCenter(c) + Vector3.up * (top - layer);
                    go.transform.localRotation = Quaternion.Euler(0f, 90f * Random.Range(0, 4), 0f);
                    go.AddComponent<MeshFilter>().sharedMesh = blockMesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    bool skull = blockSkullMaterial != null && Random.value < raisedSkullChance;
                    // 子网格：0 = 顶面和底面，1–4 = 远、右、近、左四个侧面
                    var materials = new Material[5];
                    materials[0] = blockTopMaterial;
                    for (int side = 0; side < 4; side++) materials[side + 1] = blockSideMaterials[side % blockSideMaterials.Length];
                    if (skull) materials[1 + Random.Range(0, 4)] = blockSkullMaterial;
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = squares[i].GetComponent<MeshRenderer>().receiveShadows;
                    terrainBlocks.Add(go);
                    if (layer == 0) terrainTops[i] = renderer;
                }
            }
        }

        /// <summary>方块网格和材质只建一次（材质照着格子原来的材质抄，受不受光照不变）。</summary>
        void EnsureBlockAssets(Material baseMaterial)
        {
            if (blockMesh != null) return;
            blockMesh = BuildBlockMesh();
            blockTopMaterial = new Material(baseMaterial) { hideFlags = HideFlags.DontSave };
            blockSideMaterials = new Material[raisedSideTextures.Length];
            for (int k = 0; k < raisedSideTextures.Length; k++) blockSideMaterials[k] = TexturedMaterial(baseMaterial, raisedSideTextures[k]);
            blockSkullMaterial = raisedSkullTexture != null ? TexturedMaterial(baseMaterial, raisedSkullTexture) : null;
        }

        static Material TexturedMaterial(Material baseMaterial, Texture texture)
        {
            var material = new Material(baseMaterial) { hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            return material;
        }

        /// <summary>
        /// 1 × 1 × 1 的方块，原点在顶面中心。子网格 0 = 顶面 + 底面，1–4 = +Z、+X、−Z、−X 四个侧面；
        /// 侧面的 UV 从外面看左下 (0,0) 到右上 (1,1)，贴图的顶边（草）在上面。
        /// </summary>
        static Mesh BuildBlockMesh()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var submeshes = new List<int>[5];
            for (int k = 0; k < 5; k++) submeshes[k] = new List<int>();
            void Face(Vector3 normal, Vector3 faceUp, int submesh)
            {
                var right = Vector3.Cross(faceUp, -normal);
                var center = normal * 0.5f + Vector3.down * 0.5f;
                int start = vertices.Count;
                vertices.Add(center - right * 0.5f - faceUp * 0.5f);
                vertices.Add(center - right * 0.5f + faceUp * 0.5f);
                vertices.Add(center + right * 0.5f + faceUp * 0.5f);
                vertices.Add(center + right * 0.5f - faceUp * 0.5f);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                for (int k = 0; k < 4; k++) normals.Add(normal);
                submeshes[submesh].AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            Face(Vector3.up, Vector3.forward, 0);
            Face(Vector3.down, Vector3.forward, 0);
            Face(Vector3.forward, Vector3.up, 1);
            Face(Vector3.right, Vector3.up, 2);
            Face(Vector3.back, Vector3.up, 3);
            Face(Vector3.left, Vector3.up, 4);
            var mesh = new Mesh { name = "Terrain Block", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 5;
            for (int k = 0; k < 5; k++) mesh.SetTriangles(submeshes[k], k);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>城堡上方的字：城门关着写 Castle，开了写 Gate open!</summary>
        public void RefreshCastleLabel()
        {
            if (castleLabel == null || board == null) return;
            var label = castleLabel.GetComponent<TextMesh>();
            if (label == null) return;
            bool open = board.GateOpen;
            label.text = open ? "Gate open!" : "Castle";
            label.color = open ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.85f, 0.95f);
        }

        /// <summary>新出现的岩浆格（例如爆炸留下的熔岩坑）也放上特效。</summary>
        public void AddLavaEffects(IEnumerable<Coord> squares)
        {
            if (lavaEffect == null || squares == null) return;
            foreach (var c in squares)
            {
                var fx = ParticleFx.Spawn(lavaEffect, root, PiecePosition(c) + Vector3.up * lavaEffectLift, lavaEffectScale, Random.Range(0f, 360f));
                fx.name = $"Lava Effect {c}";
                ParticleFx.Warm(fx, Random.Range(0.5f, 1.5f));
                lavaEffects.Add(fx);
            }
        }

        /// <summary>每格岩浆上放一份特效（ChessDesk.lavaEffect），旧的先清掉。</summary>
        void RebuildLavaEffects()
        {
            foreach (var fx in lavaEffects) ViewUtil.Destroy(fx);
            lavaEffects.Clear();
            if (lavaEffect == null || board == null) return;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (!board.IsLava(c)) continue;
                // 每格随机转个方向、随机快进一段：火苗各不一样、不同步，一出现就已经烧起来了
                var fx = ParticleFx.Spawn(lavaEffect, root, PiecePosition(c) + Vector3.up * lavaEffectLift, lavaEffectScale, Random.Range(0f, 360f));
                fx.name = $"Lava Effect {c}";
                ParticleFx.Warm(fx, Random.Range(2f, 6f));
                lavaEffects.Add(fx);
            }
        }

        /// <summary>
        /// 升起来的地形里必须框进画面的点（城堡顶的四角和 Princess 字样），坐标在 Chess 根物体的本地空间，
        /// 给 DeskCameraFit 用。没有城堡时为空。
        /// </summary>
        public List<Vector3> TallPoints()
        {
            var points = new List<Vector3>();
            if (board == null) return points;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (!board.IsCastle(c)) continue;
                var top = SquareCenter(c) + Vector3.up * TopOf(c);
                for (int dx = -1; dx <= 1; dx += 2)
                for (int dz = -1; dz <= 1; dz += 2)
                    points.Add(ToParent(top + new Vector3(dx * 0.5f, 0, dz * 0.5f)));
            }
            if (castleLabel != null) points.Add(ToParent(castleLabel.transform.localPosition + Vector3.up * 0.3f));
            return points;
        }

        Vector3 ToParent(Vector3 local) =>
            root.parent != null ? root.parent.InverseTransformPoint(root.TransformPoint(local)) : root.TransformPoint(local);

        /// <summary>按棋盘状态把所有棋子摆正（演出结束后调用，保证显示和逻辑一致）。</summary>
        public void SyncAll()
        {
            foreach (var piece in board.AllPieces)
            {
                pieces.TryGetValue(piece.Id, out var view);
                if (!piece.OnBoard)
                {
                    if (view != null)
                    {
                        TreeGone(piece.Id, view.Root.transform.localPosition);
                        view.Destroy();
                        pieces.Remove(piece.Id);
                    }
                    continue;
                }
                if (view == null) view = Create(piece);
                view.Root.SetActive(true);
                view.Root.transform.localScale = Vector3.one;
                view.SetPosition(PiecePosition(piece.Pos));
                Refresh(view, piece);
            }
        }

        PieceView ghost;
        int ghostPieceId = -1;
        Coord ghostAt;

        /// <summary>
        /// 在 at 上预览一枚（还没上棋盘的）棋子，外观换成 skin；at 为 null 时收起预览。
        /// 用一份拷贝显示，不改真正的棋子。
        /// </summary>
        public void ShowGhost(Piece piece, string skin, Coord? at)
        {
            if (piece != null && at.HasValue && ghost != null && ghostPieceId == piece.Id && ghostAt == at.Value) return;
            HideGhost();
            if (piece == null || !at.HasValue) return;
            var copy = piece.Clone();
            copy.OnBoard = true;
            copy.Pos = at.Value;
            copy.Skin = skin;
            copy.FrozenTurns = 0;
            copy.FrozenSkin = null;
            copy.WasLost = true;
            ghost = new PieceView(factory, pieceRoot, copy);
            ghost.Root.name = "Revive Preview";
            ghost.Refresh(copy, Stage3);
            ghost.SetPosition(PiecePosition(at.Value));
            ghost.MakeGhost();
            ghostPieceId = piece.Id;
            ghostAt = at.Value;
        }

        public void HideGhost()
        {
            if (ghost != null) ghost.Destroy();
            ghost = null;
            ghostPieceId = -1;
        }

        PieceView Create(Piece piece)
        {
            var view = new PieceView(factory, pieceRoot, piece);
            view.SetPosition(PiecePosition(piece.Pos));
            pieces[piece.Id] = view;
            return view;
        }

        /// <summary>棋盘上有棋子用 3D 模型显示（例如主教变体、消防栓、树）。</summary>
        public bool AnyModel
        {
            get
            {
                foreach (var view in pieces.Values)
                    if (view.HasModel && view.Root.activeSelf) return true;
                return false;
            }
        }

        /// <summary>棋盘已经是第三阶段：棋子换成 3D 模型。</summary>
        bool Stage3 => board != null && board.Stage3;

        public void RefreshPiece(int id)
        {
            if (pieces.TryGetValue(id, out var view)) Refresh(view, board.GetPiece(id));
        }

        /// <summary>刷新棋子的显示；兵变成树、树变回兵时在那一格播一次树特效。</summary>
        void Refresh(PieceView view, Piece piece)
        {
            view.Refresh(piece, Stage3);
            bool tree = piece.OnBoard && piece.IsFrozen && piece.FrozenSkin == "tree";
            treeShown.TryGetValue(piece.Id, out bool was);
            if (tree == was) return;
            treeShown[piece.Id] = tree;
            if (quietTrees) return;
            PlayTreeEffect(PiecePosition(piece.Pos));
            if (!tree) treeVanished = true;
        }

        /// <summary>棋子离开棋盘（被吃、掉进岩浆）时它要是树，也播一次树特效。</summary>
        void TreeGone(int id, Vector3 localPosition)
        {
            if (!treeShown.TryGetValue(id, out bool was)) return;
            treeShown.Remove(id);
            if (!was || quietTrees) return;
            PlayTreeEffect(localPosition);
            treeVanished = true;
        }

        /// <summary>消防栓从 from 朝 toward 那一格喷水的动画（长度固定，不等它播完）。</summary>
        public void PlayHydrantSpray(Coord from, Coord toward)
        {
            if (hydrantSprayFrames == null || hydrantSprayFrames.Length == 0) return;
            var localDirection = (SquareCenter(toward) - SquareCenter(from)).normalized;
            // 起点沿喷水方向往前挪一点，水柱不会从消防栓身体里冒出来
            var start = PiecePosition(from) + Vector3.up * hydrantSprayHeight + localDirection * hydrantSprayOffset;
            var direction = root.TransformDirection(localDirection);
            var spray = LineSpriteAnimation.Play(root, start, direction, hydrantSprayFrames, hydrantSprayFps, factory.Camera);
            spray.name = "Hydrant Spray";
        }

        /// <summary>棋子变回原来的样子（例如喷完火的龙变回马）：和树出现、消失一样冒一团粉尘。</summary>
        public void PlayRevertEffect(int id)
        {
            if (pieces.TryGetValue(id, out var view)) PlayTreeEffect(view.Root.transform.localPosition);
        }

        void PlayTreeEffect(Vector3 localPosition)
        {
            if (treeEffect == null) return;
            var fx = ParticleFx.Spawn(treeEffect, root, localPosition, treeEffectScale, Random.Range(0f, 360f));
            fx.name = "Tree Effect";
            // DustExplosion 里的冲击波扭曲圈太大（缩小后半径还有 4 格左右），关掉，只留粉尘和火星
            var shockwave = fx.transform.Find("Shockwave");
            if (shockwave != null) shockwave.gameObject.SetActive(false);
            ParticleFx.PlayOnce(fx);
        }

        /// <summary>
        /// 格子着色。优先级：岩浆 > 城堡 > 被将军的王 > 点选目标 > 可吃 > 可走 > 选中 > 上一步 > 普通。
        /// 第三阶段献祭完成前，我方还去不了的对面格子会暗一些；高原是岩石色。
        /// 有棋盘图时平地格子藏在图下面，这些颜色改成盖在图上的半透明色块（岩浆不透明）。
        /// </summary>
        /// <param name="effect">
        /// 消防栓、青蛙的预览：会被喷开、拉过来的棋子在 From，落到 To；Burns = 落进岩浆（To 标红）。
        /// </param>
        /// <param name="affected">技能预览：会被打到的格子（敌人红、自己人黄、空格淡红）。</param>
        public void Highlight(Coord? selected, IEnumerable<Move> moves, IEnumerable<Coord> pickOptions, Move? lastMove, Coord? danger = null,
            (Coord From, Coord To, bool Burns)? effect = null, IEnumerable<Coord> affected = null)
        {
            var colors = new Color[64];
            var marks = new Color[64]; // 有棋盘图时的色块，alpha = 0 表示不显示
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                bool dark = (c.X + c.Y) % 2 == 0;
                colors[i] = dark ? Dark : Light;
                if (board != null && board.Stage3)
                {
                    if (board.Height(c) > 0) colors[i] = hasBoardArt ? Color.Lerp(Rock, Color.black, dark ? 0.12f : 0f) : Color.Lerp(colors[i], Rock, 0.6f);
                }
            }
            if (lastMove.HasValue)
            {
                Mark(colors, marks, lastMove.Value.From.Index, LastMove, 0.6f, 0.4f);
                Mark(colors, marks, lastMove.Value.To.Index, LastMove, 0.6f, 0.4f);
            }
            if (selected.HasValue) Mark(colors, marks, selected.Value.Index, Selected, 1f, 0.55f);
            if (moves != null)
                foreach (var m in moves)
                    Mark(colors, marks, m.To.Index, board.PieceAt(m.To) != null || board.IsLava(m.To) ? CaptureTarget : MoveTarget, 1f, 0.55f);
            if (pickOptions != null)
                foreach (var c in pickOptions)
                    Mark(colors, marks, c.Index, PickTarget, 1f, 0.55f);
            if (danger.HasValue) Mark(colors, marks, danger.Value.Index, Danger, 1f, 0.6f);
            foreach (var c in warnings) Mark(colors, marks, c.Index, Warning, 0.7f, 0.5f);
            if (affected != null)
                foreach (var c in affected)
                {
                    var occupant = board?.PieceAt(c);
                    var color = occupant == null ? AffectEmpty : occupant.Side == board.Rescuer ? AffectOwn : AffectEnemy;
                    Mark(colors, marks, c.Index, color, 0.85f, occupant == null ? 0.4f : 0.7f);
                }
            bool gateOpen = board != null && board.GateOpen;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (board != null && board.IsCastle(c))
                {
                    colors[i] = gateOpen ? Color.Lerp(CastleStone, GateOpenColor, 0.6f) : CastleStone;
                    marks[i] = Color.clear;
                    if (pickOptions != null)
                        foreach (var o in pickOptions)
                            if (o == c) colors[i] = Color.Lerp(colors[i], PickTarget, 0.6f);
                    if (moves != null)
                        foreach (var m in moves)
                            if (m.To == c) colors[i] = Color.Lerp(colors[i], MoveTarget, 0.7f);
                }
                if (board != null && board.IsLava(c))
                {
                    colors[i] = Lava;
                    marks[i] = colors[i];
                }
                if (effect.HasValue)
                {
                    if (i == effect.Value.From.Index) Mark(colors, marks, i, EffectTarget, 1f, 0.6f);
                    if (i == effect.Value.To.Index) Mark(colors, marks, i, effect.Value.Burns ? Danger : EffectTarget, 1f, effect.Value.Burns ? 0.85f : 0.4f);
                }
                factory.SetColor(squares[i], colors[i]);
                // 贴图方块只给顶面上色，侧面保持贴图原色
                if (terrainTops[i] != null)
                {
                    topBlock.Clear();
                    topBlock.SetColor("_BaseColor", colors[i]);
                    topBlock.SetColor("_Color", colors[i]);
                    terrainTops[i].SetPropertyBlock(topBlock, 0);
                }
                if (overlays != null)
                {
                    overlays[i].enabled = marks[i].a > 0f;
                    overlays[i].color = marks[i];
                }
            }
        }

        /// <summary>一直标着的格子（王后预告要打的格子），下次 Highlight 时生效。</summary>
        public void SetWarnings(IEnumerable<Coord> squares)
        {
            warnings.Clear();
            if (squares != null) warnings.AddRange(squares);
        }

        /// <summary>没有棋盘图时把格子颜色往 color 混 blend；有棋盘图时在格子上盖一层 alpha 为 overlayAlpha 的色块。</summary>
        void Mark(Color[] colors, Color[] marks, int index, Color color, float blend, float overlayAlpha)
        {
            colors[index] = Color.Lerp(colors[index], color, blend);
            if (hasBoardArt) marks[index] = new Color(color.r, color.g, color.b, overlayAlpha);
        }

        // ---------- 演出 ----------

        /// <summary>潮湿的棋子遇火爆炸、自爆：在 at 播一次爆炸特效。</summary>
        public void PlayExplosion(Coord at)
        {
            if (explosionEffect == null) return;
            var fx = ParticleFx.Spawn(explosionEffect, root, PiecePosition(at) + Vector3.up * 0.3f, explosionEffectScale, Random.Range(0f, 360f));
            fx.name = "Explosion Effect";
            // SmallExplosion 的冲击波扭曲球太大，盖住一大片棋盘，关掉
            var shockwave = fx.transform.Find("Shockwave");
            if (shockwave != null) shockwave.gameObject.SetActive(false);
            ParticleFx.PlayOnce(fx);
        }

        /// <summary>下雨：在棋盘上空播一段雨，duration 秒后停。</summary>
        public void PlayRain()
        {
            if (rainEffect == null) return;
            float top = 0f;
            if (board != null)
                for (int i = 0; i < 64; i++) top = Mathf.Max(top, TopOf(Coord.FromIndex(i)));
            var fx = ParticleFx.Spawn(rainEffect, root, Vector3.up * (top + 4f), rainEffectScale);
            fx.name = "Rain Effect";
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var shape = ps.shape;
                if (shape.enabled && shape.shapeType == ParticleSystemShapeType.Box) shape.scale = new Vector3(9f, 0.1f, 9f) / Mathf.Max(0.01f, rainEffectScale);
                ps.Play(false);
            }
            // 循环的雨下 rainDuration 秒后停止发射，已经落下的雨点落完再删
            fx.AddComponent<StopAfter>().seconds = Mathf.Max(0.5f, rainDuration);
            Object.Destroy(fx, Mathf.Max(0.5f, rainDuration) + 3f);
        }

        /// <summary>象的光束：一根发光的细条从 from 打到 to，duration 秒内变细消失。</summary>
        public IEnumerator AnimateBeam(Coord from, Coord to, float duration)
        {
            var start = PiecePosition(from) + Vector3.up * 0.45f;
            var end = PiecePosition(to) + Vector3.up * 0.45f;
            var beam = factory.Primitive(PrimitiveType.Cube, root, (start + end) / 2, new Vector3(0.12f, 0.12f, (end - start).magnitude + 0.5f));
            beam.name = "Bishop Beam";
            beam.transform.localRotation = Quaternion.LookRotation(end - start);
            factory.SetColor(beam, beamColor);
            var renderer = beam.GetComponent<Renderer>();
            if (renderer != null) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, duration))
            {
                float w = 0.18f * (1f - t * t);
                beam.transform.localScale = new Vector3(w, w, beam.transform.localScale.z);
                yield return null;
            }
            ViewUtil.Destroy(beam);
        }

        /// <summary>雷暴：范围里的格子闪白光，每格播一次特效。</summary>
        public IEnumerator AnimateThunder(IReadOnlyList<Coord> area, float duration)
        {
            var flashes = new List<GameObject>();
            foreach (var c in area)
            {
                var flash = factory.Primitive(PrimitiveType.Cube, root, PiecePosition(c) + Vector3.up * 0.02f, new Vector3(0.96f, 0.03f, 0.96f));
                flash.name = "Thunder Flash";
                factory.SetColor(flash, new Color(1.6f, 1.6f, 2.2f));
                flashes.Add(flash);
                if (thunderEffect != null)
                {
                    var fx = ParticleFx.Spawn(thunderEffect, root, PiecePosition(c) + Vector3.up * 0.3f, thunderEffectScale, Random.Range(0f, 360f));
                    fx.name = "Thunder Effect";
                    ParticleFx.PlayOnce(fx);
                }
            }
            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, duration))
            {
                bool on = Mathf.Repeat(t * 6f, 1f) < 0.6f;
                foreach (var flash in flashes) flash.SetActive(on && t < 0.85f);
                yield return null;
            }
            foreach (var flash in flashes) ViewUtil.Destroy(flash);
        }

        public IEnumerator AnimateMove(int id, Coord from, Coord to, float duration)
        {
            var piece = board.GetPiece(id);
            if (!pieces.TryGetValue(id, out var view)) view = Create(piece);
            var start = view.Root.transform.localPosition;
            var end = PiecePosition(to);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                float k = Mathf.SmoothStep(0, 1, t);
                view.SetPosition(Vector3.Lerp(start, end, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.35f);
                yield return null;
            }
            view.SetPosition(end);
        }

        /// <summary>被爆炸炸开：抛出去落到 to（掉进岩浆的话由随后的 Captured 事件烧掉）；to 就是原地时只是被震得跳一下。</summary>
        public IEnumerator AnimateKnockback(int id, Coord to, float duration)
        {
            if (!pieces.TryGetValue(id, out var view)) yield break;
            var start = view.Root.transform.localPosition;
            var end = PiecePosition(to);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                view.SetPosition(Vector3.Lerp(start, end, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.2f);
                view.Root.transform.localRotation = Quaternion.Euler(0, 0, 360f * t);
                yield return null;
            }
            view.Root.transform.localRotation = Quaternion.identity;
            view.SetPosition(end);
        }

        /// <summary>被消防栓喷开：贴着棋盘滑过去，先快后慢，最后晃一下。</summary>
        public IEnumerator AnimatePush(int id, Coord to, float duration)
        {
            if (!pieces.TryGetValue(id, out var view)) yield break;
            var start = view.Root.transform.localPosition;
            var end = PiecePosition(to);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                float k = 1f - (1f - t) * (1f - t);
                view.SetPosition(Vector3.Lerp(start, end, k));
                view.Root.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * Mathf.PI * 3f) * 12f * (1f - t));
                yield return null;
            }
            view.Root.transform.localRotation = Quaternion.identity;
            view.SetPosition(end);
        }

        IEnumerator MoveView(PieceView view, Vector3 end, float duration)
        {
            var start = view.Root.transform.localPosition;
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                float k = Mathf.SmoothStep(0, 1, t);
                view.SetPosition(Vector3.Lerp(start, end, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.35f);
                yield return null;
            }
            view.SetPosition(end);
        }

        public IEnumerator AnimateCapture(int id, bool burned, float duration)
        {
            if (!pieces.TryGetValue(id, out var view)) yield break;
            pieces.Remove(id);
            TreeGone(id, view.Root.transform.localPosition);
            if (burned && dissolveMaterial != null)
            {
                yield return Dissolve(view);
                view.Destroy();
                yield break;
            }
            if (burned)
            {
                var flame = factory.Primitive(PrimitiveType.Sphere, view.Root.transform, new Vector3(0, 0.4f, 0), Vector3.one * 0.9f);
                factory.SetColor(flame, new Color(1f, 0.5f, 0.05f));
                yield return Wait(duration);
            }
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                view.Root.transform.localScale = Vector3.one * (1f - t);
                yield return null;
            }
            view.Destroy();
        }

        /// <summary>
        /// 被烧掉：棋子身上所有的图和模型换成溶解材质（贴图、颜色照抄），按噪声一块块镂空，边缘发光，同时飘一次火星。
        /// 棋子上的小字（剩几回合之类）直接收起来。
        /// </summary>
        IEnumerator Dissolve(PieceView view)
        {
            var materials = new List<Material>();
            var noiseOffset = new Vector4(Random.value * 10f, Random.value * 10f, Random.value * 10f, 0f);
            foreach (var renderer in view.Root.GetComponentsInChildren<Renderer>())
            {
                if (renderer.GetComponent<TextMesh>() != null)
                {
                    renderer.enabled = false;
                    continue;
                }
                var originals = renderer.sharedMaterials;
                var replaced = new Material[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                {
                    var material = new Material(dissolveMaterial);
                    var original = originals[i];
                    // 贴图棋子的贴图由 SpriteRenderer 自己塞进 _MainTex；模型要把原材质的贴图、颜色抄过来
                    if (original != null && !(renderer is SpriteRenderer))
                    {
                        var texture = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : original.mainTexture;
                        if (texture != null) material.SetTexture(MainTexId, texture);
                        if (original.HasProperty(BaseColorId)) material.SetColor(BaseColorId, original.GetColor(BaseColorId));
                        else if (original.HasProperty("_Color")) material.SetColor(BaseColorId, original.color);
                    }
                    material.SetVector(NoiseOffsetId, noiseOffset);
                    replaced[i] = material;
                    materials.Add(material);
                }
                renderer.sharedMaterials = replaced;
            }

            if (dissolveParticles != null)
            {
                var fx = ParticleFx.Spawn(dissolveParticles, root, view.Root.transform.localPosition + Vector3.up * 0.4f, dissolveParticlesScale);
                fx.name = "Dissolve Effect";
                // ParticlePack 的 Dissolve 根物体上就是演示用的球和让它循环重播的脚本，都不要
                foreach (var r in fx.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
                foreach (var script in fx.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
                ParticleFx.PlayOnce(fx);
            }

            float duration = Mathf.Max(0.05f, dissolveDuration);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                foreach (var material in materials) material.SetFloat(ProgressId, t);
                yield return null;
            }
            foreach (var material in materials) ViewUtil.Destroy(material);
        }

        public IEnumerator AnimateSpawn(int id, float duration)
        {
            var piece = board.GetPiece(id);
            if (!pieces.TryGetValue(id, out var view)) view = Create(piece);
            view.SetPosition(PiecePosition(piece.Pos));
            Refresh(view, piece);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                view.Root.transform.localScale = Vector3.one * Mathf.SmoothStep(0, 1, t);
                yield return null;
            }
            view.Root.transform.localScale = Vector3.one;
        }

        /// <summary>外观变化时轻轻弹一下，让玩家注意到。</summary>
        public IEnumerator AnimatePop(int id, float duration)
        {
            if (!pieces.TryGetValue(id, out var view)) yield break;
            Refresh(view, board.GetPiece(id));
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                view.Root.transform.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(t * Mathf.PI));
                yield return null;
            }
            view.Root.transform.localScale = Vector3.one;
        }

        static IEnumerator Wait(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime) yield return null;
        }
    }
}
