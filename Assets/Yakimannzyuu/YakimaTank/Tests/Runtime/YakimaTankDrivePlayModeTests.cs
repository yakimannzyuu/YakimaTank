using UnityEditor;
using UnityEngine.TestTools;
using Yakimannzyuu.YakimaTank.Editor;
using Yakimannzyuu.YakimaTank.Editor.Tests;

namespace Yakimannzyuu.YakimaTank.Tests.PlayMode
{
    /// <summary>立方体と円柱のテスト用モデルから生成した車両で、走行の判定を確かめる。</summary>
    public class YakimaTankDrivePlayModeTests : YakimaTankDriveTestBase, IPrebuildSetup, IPostBuildCleanup
    {
        private const string WorkspaceName = "PlayMode";

        public YakimaTankDrivePlayModeTests() : base("YakimaTankDriveTest")
        {
        }

        public void Setup()
        {
            var workspace = new TestWorkspace(WorkspaceName);
            YakimaTankSetupSettings settings = workspace.CreateSettings(workspace.CreateModel());
            SetupResult result = YakimaTankSetupService.Generate(settings);
            if (!result.Succeeded)
            {
                throw new System.InvalidOperationException("テスト用の車両を生成できません: " + result.Error);
            }
        }

        public void Cleanup()
        {
            new TestWorkspace(WorkspaceName).Dispose();
            TestWorkspace.DeleteRootIfEmpty();
        }

        protected override YakimaTankSetupSettings LoadSettings()
        {
            return AssetDatabase.LoadAssetAtPath<YakimaTankSetupSettings>(new TestWorkspace(WorkspaceName).Folder + "/Settings.asset");
        }
    }
}
