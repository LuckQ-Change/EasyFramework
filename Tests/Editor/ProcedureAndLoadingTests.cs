using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public sealed class ProcedureAndLoadingTests
    {
        private sealed class FirstProcedure : ProcedureBase
        {
            public static int EnterCount;
            public FirstProcedure() { }

            protected override Task<Type> OnEnterAsync(CancellationToken cancellationToken)
            {
                EnterCount++;
                return Next<SecondProcedure>();
            }
        }

        private sealed class SecondProcedure : ProcedureBase
        {
            public static int EnterCount;
            public SecondProcedure() { }

            protected override Task<Type> OnEnterAsync(CancellationToken cancellationToken)
            {
                EnterCount++;
                return Stay();
            }
        }

        [Test]
        public void ProcedureModule_FollowsAutomaticChain_AndStopsAtStay()
        {
            var modules = new ModuleManager();
            var preset = ScriptableObject.CreateInstance<EasyFrameworkStartupPreset>();
            try
            {
                FirstProcedure.EnterCount = 0;
                SecondProcedure.EnterCount = 0;
                ProcedureModule procedures = modules.Register<ProcedureModule>();
                modules.InitAll();
                procedures.Register<FirstProcedure>();
                procedures.Register<SecondProcedure>();

                procedures.StartAsync<FirstProcedure>(new ProcedureContext(preset))
                    .GetAwaiter().GetResult();

                Assert.AreEqual(1, FirstProcedure.EnterCount);
                Assert.AreEqual(1, SecondProcedure.EnterCount);
                Assert.AreEqual(typeof(SecondProcedure), procedures.ActiveProcedureType);
            }
            finally
            {
                modules.ShutdownAll();
                UnityEngine.Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void LoadingModule_NestedScopesKeepGlobalLoadingVisible()
        {
            var modules = new ModuleManager();
            try
            {
                LoadingModule loading = modules.Register<LoadingModule>();
                modules.InitAll();

                IDisposable outer = loading.Begin("正在启动");
                IDisposable inner = loading.Begin("正在预加载");
                loading.Report(0.4f);
                Assert.IsTrue(loading.IsLoading);
                Assert.AreEqual(0.4f, loading.Progress);
                Assert.AreEqual("正在预加载", loading.Message);

                inner.Dispose();
                Assert.IsTrue(loading.IsLoading);
                outer.Dispose();
                Assert.IsFalse(loading.IsLoading);
                Assert.AreEqual(1f, loading.Progress);
            }
            finally { modules.ShutdownAll(); }
        }
    }
}
