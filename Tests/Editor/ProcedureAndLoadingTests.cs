using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public sealed class ProcedureAndLoadingTests
    {
        private sealed class ProjectScript
        {
            public int CallCount;
        }

        private sealed class ProjectProcedureContext : ProcedureContext
        {
            public ProjectScript Script { get; }
            public int DisposeCount { get; private set; }

            public ProjectProcedureContext(ProjectScript script) => Script = script;

            protected override void OnDispose() => DisposeCount++;
        }

        private sealed class FirstProcedure : ProcedureBase
        {
            public static int EnterCount;
            public FirstProcedure() { }

            protected override Task<Type> OnEnterAsync(CancellationToken cancellationToken)
            {
                EnterCount++;
                RequireContext<ProjectProcedureContext>().Script.CallCount++;
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
        public void ProcedureModule_AutoRegistersProcedureChain_AndStopsAtStay()
        {
            var modules = new ModuleManager();
            var projectScript = new ProjectScript();
            var context = new ProjectProcedureContext(projectScript);
            try
            {
                FirstProcedure.EnterCount = 0;
                SecondProcedure.EnterCount = 0;
                ProcedureModule procedures = modules.Register<ProcedureModule>();
                modules.InitAll();
                procedures.StartAsync<FirstProcedure>(context)
                    .GetAwaiter().GetResult();

                Assert.AreEqual(1, FirstProcedure.EnterCount);
                Assert.AreEqual(1, SecondProcedure.EnterCount);
                Assert.AreEqual(1, projectScript.CallCount);
                Assert.AreEqual(typeof(SecondProcedure), procedures.ActiveProcedureType);

                modules.ShutdownAll();
                Assert.IsTrue(context.IsDisposed);
                Assert.AreEqual(1, context.DisposeCount);
            }
            finally
            {
                modules.ShutdownAll();
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

        [Test]
        public void LoadingModule_ListenerFailureDoesNotBlockCleanupOrOtherListeners()
        {
            var modules = new ModuleManager();
            LoadingModule loading = modules.Register<LoadingModule>();
            modules.InitAll();
            int observed = 0;
            loading.Changed += _ => throw new InvalidOperationException("listener");
            loading.Changed += _ => observed++;
            IDisposable scope = loading.Begin("测试");
            loading.Report(0.5f);
            scope.Dispose();
            Assert.AreEqual(3, observed);
            Assert.IsFalse(loading.IsLoading);
            modules.ShutdownAll();
            loading.Report(0.2f);
            Assert.IsFalse(loading.IsLoading);
            Assert.Throws<InvalidOperationException>(() => loading.Begin());
        }

        [Test]
        public void ProcedureStart_PreCanceledTokenLeavesContextWithCaller()
        {
            var modules = new ModuleManager();
            ProcedureModule procedures = modules.Register<ProcedureModule>();
            modules.InitAll();
            var context = new ProjectProcedureContext(new ProjectScript());
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    procedures.StartAsync<FirstProcedure>(context, canceled.Token));
            }
            Assert.IsFalse(context.IsDisposed);
            Assert.IsNull(procedures.ActiveProcedure);
            context.Dispose();
            modules.ShutdownAll();
        }
    }
}
