using NUnit.Framework;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    /// <summary>この名前空間のテストがすべて終わった後に、共通の一時フォルダを消す。</summary>
    [SetUpFixture]
    public sealed class TestWorkspaceCleanup
    {
        [OneTimeTearDown]
        public void DeleteRoot()
        {
            TestWorkspace.DeleteRootIfEmpty();
        }
    }
}
