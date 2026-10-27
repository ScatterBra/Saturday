using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 生成占位用的几何体和文字。颜色用 MaterialPropertyBlock 改，所有物体共用一个不受光照影响的材质；
    /// 打开阴影时桌面、棋盘格子这些“地面”改用受光照的材质，才接得住 3D 模型投下的影子。
    /// </summary>
    public sealed class ChessViewFactory
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColor = Shader.PropertyToID("_Color");

        readonly Material material;
        readonly Material litMaterial;
        readonly Font font;
        readonly Font titleFont;
        readonly Transform cameraTransform;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        /// <summary>棋子贴图配置，为 null 时全部用占位图形。</summary>
        public readonly PieceArt Art;

        /// <summary>棋子贴图和文字固定用的俯仰角：开局时相机的俯仰角，之后相机再倾斜它们也不跟着倾斜。</summary>
        public readonly float FlatPitch = 90f;

        /// <summary>相机现在的水平转角（度），立牌模型按它摆正面。</summary>
        public float CameraYaw => cameraTransform != null ? FaceCamera.Rotation(cameraTransform, 0f).eulerAngles.y : 0f;

        /// <summary>3D 模型是否投影子。</summary>
        public readonly bool Shadows;

        public ChessViewFactory(Material material, Camera camera, PieceArt art = null, Material surfaceMaterial = null, bool shadows = false,
            Font titleFont = null, Font bodyFont = null)
        {
            Art = art;
            Shadows = shadows;
            // 运行时建的材质不存进场景（编辑器预览里也会建）
            this.material = material != null ? material : new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.DontSave };
            if (shadows)
            {
                litMaterial = surfaceMaterial;
                if (litMaterial == null)
                {
                    litMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
                    litMaterial.SetFloat("_Smoothness", 0.1f);
                }
            }
            font = bodyFont != null ? bodyFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            this.titleFont = titleFont;
            cameraTransform = camera != null ? camera.transform : null;
            if (cameraTransform != null) FlatPitch = FaceCamera.PitchOf(cameraTransform);
        }

        /// <summary>让物体朝着相机（跟着相机水平转，俯仰角固定在 FlatPitch）。</summary>
        void Face(GameObject go)
        {
            if (cameraTransform == null)
            {
                go.transform.rotation = Quaternion.Euler(90, 0, 0);
                return;
            }
            go.transform.rotation = FaceCamera.Rotation(cameraTransform, FlatPitch);
            var face = go.AddComponent<FaceCamera>();
            face.target = cameraTransform;
            face.pitch = FlatPitch;
        }

        /// <param name="surface">桌面、棋盘格子这类地面：打开阴影时用受光照的材质并接收影子。</param>
        public GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, bool surface = false)
        {
            var go = GameObject.CreatePrimitive(type);
            ViewUtil.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            var renderer = go.GetComponent<Renderer>();
            bool lit = surface && litMaterial != null;
            renderer.sharedMaterial = lit ? litMaterial : material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = lit;
            return go;
        }

        public void SetColor(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            block.SetColor(LegacyColor, color);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>给模型的所有部件换上颜色（用 MaterialPropertyBlock，不改共用的材质）；白色 = 还原成材质本来的颜色。</summary>
        public void TintModel(GameObject go, Color color)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            {
                if (color == Color.white)
                {
                    renderer.SetPropertyBlock(null);
                    continue;
                }
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                block.SetColor(LegacyColor, color);
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>始终朝向镜头的 3D 文字。</summary>
        /// <param name="title">用标题字体（醒目的短字，例如 Princess、棋子上的小字）；没有标题字体时用正文字体加粗。</param>
        public TextMesh Label(Transform parent, Vector3 localPosition, float characterSize, string text, bool title = false)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            Face(go);
            var mesh = go.AddComponent<TextMesh>();
            var useFont = title && titleFont != null ? titleFont : font;
            mesh.font = useFont;
            if (title && titleFont == null) mesh.fontStyle = FontStyle.Bold;
            mesh.fontSize = 64;
            mesh.characterSize = characterSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.text = text;
            mesh.color = Color.black;
            go.GetComponent<MeshRenderer>().sharedMaterial = useFont.material;
            return mesh;
        }

        /// <summary>始终朝向镜头的贴图，枢轴在贴图底边中点（由贴图导入设置决定）。</summary>
        public SpriteRenderer Billboard(Transform parent, Vector3 localPosition)
        {
            var go = new GameObject("Sprite");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            Face(go);
            return go.AddComponent<SpriteRenderer>();
        }

        /// <summary>
        /// 放一个 3D 模型：按包围盒缩放到 height 高、最宽不超过 width，底部中心对准格子中心，再绕竖轴转 yaw 度。
        /// 模型本身的大小、原点位置不用管，导进来的 FBX 直接拖进 PieceArt 就行。
        /// </summary>
        public GameObject Model(GameObject source, Transform parent, float height, float width, float yaw)
        {
            var holder = new GameObject("Model");
            holder.transform.SetParent(parent, false);
            var model = Object.Instantiate(source, holder.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = source.transform.localRotation;
            model.transform.localScale = source.transform.localScale;
            foreach (var collider in model.GetComponentsInChildren<Collider>()) ViewUtil.Destroy(collider);

            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var r in renderers)
                {
                    bounds.Encapsulate(r.bounds);
                    r.shadowCastingMode = Shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                // 包围盒换算到 holder 的本地坐标
                var min = holder.transform.InverseTransformPoint(bounds.min);
                var max = holder.transform.InverseTransformPoint(bounds.max);
                var size = new Vector3(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), Mathf.Abs(max.z - min.z));
                float scale = Mathf.Min(height / Mathf.Max(0.0001f, size.y), width / Mathf.Max(0.0001f, Mathf.Max(size.x, size.z)));
                var bottomCenter = new Vector3((min.x + max.x) / 2, Mathf.Min(min.y, max.y), (min.z + max.z) / 2);
                model.transform.localScale *= scale;
                model.transform.localPosition = -bottomCenter * scale;
            }
            holder.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return holder;
        }
    }
}
