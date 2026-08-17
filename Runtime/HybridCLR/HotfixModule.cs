using System;
using System.Collections.Generic;
using System.Reflection;

namespace EasyFramework
{
    public class HotfixModule : ModuleSingleton<HotfixModule>
    {
        private IHotfixLoader _loader;
        private readonly List<Assembly> _loaded = new List<Assembly>();

        public IReadOnlyList<Assembly> LoadedAssemblies => _loaded;

        public void SetLoader(IHotfixLoader loader)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        public void LoadAotMetadata(IEnumerable<byte[]> aotDlls)
        {
            EnsureLoader();
            if (aotDlls == null) return;
            foreach (var dll in aotDlls)
            {
                if (dll == null || dll.Length == 0) continue;
                _loader.LoadMetadataForAOT(dll);
            }
        }

        public Assembly LoadHotfix(byte[] dllBytes)
        {
            EnsureLoader();
            if (dllBytes == null || dllBytes.Length == 0)
            {
                Log.Error("[Hotfix] empty dll bytes.");
                return null;
            }
            try
            {
                var asm = _loader.LoadAssembly(dllBytes);
                _loaded.Add(asm);
                Log.Info($"[Hotfix] loaded {asm.GetName().Name}");
                return asm;
            }
            catch (Exception ex)
            {
                Log.Error("[Hotfix] load failed.", ex);
                return null;
            }
        }

        public Assembly FindAssembly(string name)
        {
            for (int i = 0; i < _loaded.Count; i++)
            {
                if (_loaded[i].GetName().Name == name) return _loaded[i];
            }
            return null;
        }

        public object InvokeEntry(Assembly asm, string typeFullName, string methodName, params object[] args)
        {
            if (asm == null)
            {
                Log.Error("[Hotfix] InvokeEntry: assembly is null");
                return null;
            }
            var type = asm.GetType(typeFullName);
            if (type == null)
            {
                Log.Error($"[Hotfix] type not found: {typeFullName}");
                return null;
            }
            var method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance);
            if (method == null)
            {
                Log.Error($"[Hotfix] method not found: {typeFullName}.{methodName}");
                return null;
            }
            try
            {
                object target = method.IsStatic ? null : Activator.CreateInstance(type);
                return method.Invoke(target, args);
            }
            catch (Exception ex)
            {
                Log.Error("[Hotfix] invoke error.", ex);
                return null;
            }
        }

        protected override void OnShutdown()
        {
            _loaded.Clear();
            _loader = null;
        }

        private void EnsureLoader()
        {
            if (_loader == null) _loader = new DefaultHotfixLoader();
        }
    }
}
