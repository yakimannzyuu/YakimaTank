using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    /// <summary>
    /// テスト用の一時フォルダと、立方体と円柱だけでできた戦車のモデル（権利上公開できる素材）。
    /// 左右の転輪は同じ名前「Wheel」を 3 つずつ並べ、名前だけでは区別できないモデルにしている。
    /// </summary>
    public sealed class TestWorkspace : IDisposable
    {
        public const string Root = "Assets/__YakimaTankTests";
        public const float WheelRadius = 0.4f;
        public const string TurretPath = "Hull/Turret";

        /// <summary>砲塔の上面の高さ。車体の上 1.5 m に、縦 0.6 を車体の 0.8 倍した 0.48 の半分を足す。</summary>
        public const float TurretTop = 1.74f;

        public TestWorkspace(string name = null)
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(Root));
            }

            string folderName = name ?? Guid.NewGuid().ToString("N").Substring(0, 8);
            Folder = Root + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder(Root, folderName);
            }
        }

        public string Folder { get; }

        public string OutputPath => Folder + "/Generated/Vehicle.prefab";

        /// <summary>
        /// rootRotation は、FBX の軸の変換のようにモデルのルート自身が持つ回転。
        /// withColliders なら、車体にトリガーでない BoxCollider、砲塔にトリガーの SphereCollider を残す（コライダー付きのモデル）。
        /// </summary>
        public GameObject CreateModel(string name = "TestTank", Quaternion? rootRotation = null, bool withColliders = false)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = Node(preview, name, null, Vector3.zero);
                root.transform.localRotation = rootRotation ?? Quaternion.identity;
                GameObject hull = Primitive(preview, PrimitiveType.Cube, "Hull", root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(2f, 0.8f, 4f));
                GameObject turret = Primitive(preview, PrimitiveType.Cube, "Turret", hull.transform, new Vector3(0f, 0.75f, 0f), new Vector3(0.6f, 0.6f, 0.4f));
                if (withColliders)
                {
                    hull.AddComponent<BoxCollider>();
                    turret.AddComponent<SphereCollider>().isTrigger = true;
                }
                GameObject left = Node(preview, "Left", root.transform, Vector3.zero);
                GameObject right = Node(preview, "Right", root.transform, Vector3.zero);
                foreach (float z in new[] { -1.2f, 0f, 1.2f })
                {
                    Wheel(preview, left.transform, new Vector3(-1.15f, WheelRadius, z));
                    Wheel(preview, right.transform, new Vector3(1.15f, WheelRadius, z));
                }

                string path = Folder + "/" + name + ".prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return prefab;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static GameObject Node(Scene scene, string name, Transform parent, Vector3 position)
        {
            GameObject node = ObjectFactory.CreateGameObject(scene, HideFlags.None, name);
            node.transform.SetParent(parent, false);
            node.transform.localPosition = position;
            return node;
        }

        private static GameObject Primitive(Scene scene, PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale)
        {
            GameObject primitive = ObjectFactory.CreatePrimitive(type);
            SceneManager.MoveGameObjectToScene(primitive, scene);
            primitive.name = name;
            Object.DestroyImmediate(primitive.GetComponent<Collider>());
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = position;
            primitive.transform.localScale = scale;
            return primitive;
        }

        private static void Wheel(Scene scene, Transform parent, Vector3 position)
        {
            GameObject wheel = Primitive(scene, PrimitiveType.Cylinder, "Wheel", parent, position, new Vector3(WheelRadius * 2f, 0.15f, WheelRadius * 2f));
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        /// <summary>モデルの対応を全部埋めた設定。軽い車両の値にして、テストの時間内に動きが見えるようにする。</summary>
        public YakimaTankSetupSettings CreateSettings(GameObject model, string name = "Settings")
        {
            var settings = ScriptableObject.CreateInstance<YakimaTankSetupSettings>();
            settings.modelPrefab = model;
            settings.body = model.transform.Find("Hull");
            settings.leftWheels = Children(model.transform.Find("Left"));
            settings.rightWheels = Children(model.transform.Find("Right"));
            settings.turret = model.transform.Find(TurretPath);
            settings.barrel = null;
            settings.outputPrefabPath = OutputPath;
            AssetDatabase.CreateAsset(settings, Folder + "/" + name + ".asset");
            return settings;
        }

        private static Transform[] Children(Transform parent)
        {
            return Enumerable.Range(0, parent.childCount).Select(parent.GetChild).ToArray();
        }

        /// <summary>
        /// 開いているシーンを空のシーン 1 つに置き換える。追加で開くと、Test Runner が開いた無題のシーンがあるときに失敗する。
        /// 利用者のシーンは Test Runner が実行の後に開き直す。
        /// </summary>
        public static Scene OpenEmptyScene()
        {
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>
        /// 積まれた delayCall が走るまで待つ。UdonSharp は部品を足した後の処理を delayCall で行い、
        /// その前に一時のフォルダを消すと、消えたオブジェクトに触れて例外のログを出す。
        /// </summary>
        public static IEnumerator WaitForDelayCalls()
        {
            bool done = false;
            EditorApplication.delayCall += () => done = true;
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (!done && EditorApplication.timeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        public static string Hash(string assetPath)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(assetPath)));
            }
        }

        /// <summary>
        /// テストごとのフォルダだけを消す。共通のフォルダはテストごとに消さない。消した直後に作り直すと、
        /// Windows で消える途中のフォルダに書けず、.meta の書き込みに失敗することがあった。
        /// </summary>
        public void Dispose()
        {
            AssetDatabase.DeleteAsset(Folder);
        }

        /// <summary>空になった共通のフォルダを消す。実行の終わりに 1 回だけ呼ぶ。</summary>
        public static void DeleteRootIfEmpty()
        {
            if (AssetDatabase.IsValidFolder(Root) && AssetDatabase.FindAssets(string.Empty, new[] { Root }).Length == 0)
            {
                AssetDatabase.DeleteAsset(Root);
            }
        }
    }
}
