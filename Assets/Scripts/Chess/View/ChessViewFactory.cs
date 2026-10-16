using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 生成占位用的几何体和文字。颜色用 MaterialPropertyBlock 改，所有物体共用一个不受光照影响的材质。
    /// </summary>
    public sealed class ChessViewFactory
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColor = Shader.PropertyToID("_Color");

        readonly Material material;
        readonly Font font;
        readonly Quaternion labelRotation;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        /// <summary>棋子贴图配置，为 null 时全部用占位图形。</summary>
        public readonly PieceArt Art;

        public ChessViewFactory(Material material, Camera camera, PieceArt art = null)
        {
            Art = art;
            this.material = material != null ? material : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelRotation = camera != null ? camera.transform.rotation : Quaternion.Euler(90, 0, 0);
        }

        public GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
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

        /// <summary>始终朝向镜头的 3D 文字。</summary>
        public TextMesh Label(Transform parent, Vector3 localPosition, float characterSize, string text)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.rotation = labelRotation;
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.fontSize = 64;
            mesh.characterSize = characterSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.text = text;
            mesh.color = Color.black;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return mesh;
        }

        /// <summary>始终朝向镜头的贴图，枢轴在贴图底边中点（由贴图导入设置决定）。</summary>
        public SpriteRenderer Billboard(Transform parent, Vector3 localPosition)
        {
            var go = new GameObject("Sprite");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.rotation = labelRotation;
            return go.AddComponent<SpriteRenderer>();
        }
    }
}
