using System.Reflection;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public sealed class HotfixModuleTests
    {
        private sealed class EmptyLoader : IHotfixLoader
        {
            public void LoadMetadataForAOT(byte[] dllBytes) { }
            public Assembly LoadAssembly(byte[] dllBytes) => null;
        }

        private sealed class AmbiguousEntry
        {
            public static object Run(int value) => value;
            public static object Run(string value) => value;
        }

        [Test]
        public void NullAssemblyAndAmbiguousEntry_DoNotCorruptLoadedList()
        {
            var manager = new ModuleManager();
            HotfixModule hotfix = manager.Register<HotfixModule>();
            manager.InitAll();
            try
            {
                hotfix.SetLoader(new EmptyLoader());
                Assert.IsNull(hotfix.LoadHotfix(new byte[] { 1 }));
                Assert.AreEqual(0, hotfix.LoadedAssemblies.Count);
                Assert.IsNull(hotfix.FindAssembly("Missing"));
                Assert.IsNull(hotfix.InvokeEntry(typeof(AmbiguousEntry).Assembly,
                    typeof(AmbiguousEntry).FullName, "Run", 1));
            }
            finally { manager.ShutdownAll(); }
        }
    }
}
