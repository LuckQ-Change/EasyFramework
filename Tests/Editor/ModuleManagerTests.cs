using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class ModuleManagerTests
    {
        private static readonly List<string> Calls = new List<string>();

        public sealed class FirstModule : ModuleSingleton<FirstModule>
        {
            protected override void OnInit()
            {
                Assert.IsNotNull(SecondModule.Instance);
                Calls.Add("first.init");
            }

            protected override void OnShutdown() => Calls.Add("first.shutdown");
        }

        public sealed class SecondModule : ModuleSingleton<SecondModule>
        {
            protected override void OnInit() => Calls.Add("second.init");
            protected override void OnShutdown() => Calls.Add("second.shutdown");
        }

        public sealed class FailingModule : ModuleSingleton<FailingModule>
        {
            protected override void OnInit() => throw new InvalidOperationException("expected");
            protected override void OnShutdown() => Calls.Add("failing.shutdown");
        }

        public sealed class LateModule : ModuleSingleton<LateModule>
        {
            public bool Initialized { get; private set; }
            protected override void OnInit() => Initialized = true;
        }

        [SetUp]
        public void SetUp()
        {
            Calls.Clear();
        }

        [Test]
        public void InitAll_RegistersEverythingBeforeInitializing_AndShutsDownInReverse()
        {
            var manager = new ModuleManager();
            manager.Register<FirstModule>();
            manager.Register<SecondModule>();

            manager.InitAll();
            CollectionAssert.AreEqual(new[] { "first.init", "second.init" }, Calls);
            Assert.AreEqual(ModuleManagerState.Running, manager.State);

            manager.ShutdownAll();
            CollectionAssert.AreEqual(
                new[] { "first.init", "second.init", "second.shutdown", "first.shutdown" },
                Calls);
            Assert.IsNull(FirstModule.Instance);
            Assert.IsNull(SecondModule.Instance);
        }

        [Test]
        public void InitAll_WhenModuleFails_RollsBackEveryRegisteredSingleton()
        {
            var manager = new ModuleManager();
            manager.Register<FirstModule>();
            manager.Register<SecondModule>();
            manager.Register<FailingModule>();

            Assert.Throws<InvalidOperationException>(() => manager.InitAll());

            Assert.AreEqual(ModuleManagerState.Faulted, manager.State);
            Assert.AreEqual(0, manager.Count);
            Assert.IsNull(FirstModule.Instance);
            Assert.IsNull(SecondModule.Instance);
            Assert.IsNull(FailingModule.Instance);
            CollectionAssert.Contains(Calls, "failing.shutdown");
        }

        [Test]
        public void Install_InitializesLateModulesAsOneBatch()
        {
            var manager = new ModuleManager();
            manager.InitAll();

            manager.Install(modules =>
            {
                modules.Register<FirstModule>();
                modules.Register<SecondModule>();
            });

            Assert.AreEqual(ModuleManagerState.Running, manager.State);
            CollectionAssert.AreEqual(new[] { "first.init", "second.init" }, Calls);
            manager.ShutdownAll();
        }

        [Test]
        public void RegistrarAddedAfterStartup_IsInstalledImmediately()
        {
            ModuleRegistry.ClearRegistrars();
            try
            {
                EasyEntry.Init(ModuleRegistry.ApplyAll);
                Assert.IsNull(LateModule.Instance);

                ModuleRegistry.AddRegistrar(modules => modules.Register<LateModule>());

                Assert.IsNotNull(LateModule.Instance);
                Assert.IsTrue(LateModule.Instance.Initialized);
            }
            finally
            {
                EasyEntry.Shutdown();
                ModuleRegistry.ClearRegistrars();
            }
        }
    }
}
