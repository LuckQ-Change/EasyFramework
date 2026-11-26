using System;
using System.Collections.Generic;

namespace EasyFramework
{
    /// <summary>声明模块工厂和必需依赖，供宿主在启动前检查和排序。</summary>
    public sealed class ModuleComposition
    {
        private readonly List<ModuleRegistration> _registrations = new();

        #region 公共方法

        /// <summary>登记由无参构造函数创建的模块。</summary>
        /// <typeparam name="T">模块的注册类型。</typeparam>
        /// <param name="dependencies">模块初始化前必须就绪的注册类型。</param>
        /// <returns>当前装配声明。</returns>
        public ModuleComposition Add<T>(params Type[] dependencies) where T : Module, new() =>
            Add<T>(_ => new T(), dependencies);

        /// <summary>登记由工厂创建的模块。工厂执行时可以通过宿主获取已就绪的依赖。</summary>
        /// <typeparam name="T">模块的注册类型。</typeparam>
        /// <param name="factory">每个宿主分别调用一次的模块工厂。</param>
        /// <param name="dependencies">模块初始化前必须就绪的注册类型。</param>
        /// <returns>当前装配声明。</returns>
        public ModuleComposition Add<T>(Func<ModuleManager, T> factory, params Type[] dependencies) where T : Module
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var registration = new ModuleRegistration(typeof(T), factory, dependencies);
            lock (_registrations)
            {
                foreach (ModuleRegistration existing in _registrations)
                    if (existing.Type == registration.Type)
                        throw new InvalidOperationException($"模块 {registration.Type.FullName} 已经声明。");
                _registrations.Add(registration);
            }
            return this;
        }

        #endregion

        internal ModuleRegistration[] CopyRegistrations()
        {
            lock (_registrations) return _registrations.ToArray();
        }
    }

    /// <summary>一条待装配模块的类型、依赖和创建方式。</summary>
    internal sealed class ModuleRegistration
    {
        internal Type Type { get; }
        internal Func<ModuleManager, Module> Factory { get; }
        internal IReadOnlyList<Type> Dependencies { get; }

        internal ModuleRegistration(Type type, Func<ModuleManager, Module> factory, Type[] dependencies)
        {
            Type = type;
            Factory = factory;
            Dependencies = ValidateDependencies(dependencies);
        }

        private static IReadOnlyList<Type> ValidateDependencies(Type[] dependencies)
        {
            if (dependencies == null) throw new ArgumentNullException(nameof(dependencies));
            if (dependencies.Length == 0) return Array.Empty<Type>();

            var result = new List<Type>(dependencies.Length);
            foreach (Type dependency in dependencies)
            {
                if (dependency == null || !typeof(Module).IsAssignableFrom(dependency) || dependency.ContainsGenericParameters)
                    throw new ArgumentException("模块依赖必须使用有效的模块注册类型。", nameof(dependencies));
                if (!result.Contains(dependency)) result.Add(dependency);
            }
            return result;
        }
    }

    /// <summary>在模块创建和初始化前校验依赖，并生成可并行初始化的阶段。</summary>
    internal static class ModuleStartupPlan
    {
        /// <summary>追踪待启动模块的依赖数量和下游关系。</summary>
        private sealed class Node
        {
            internal readonly ModuleRegistration Registration;
            internal readonly List<Node> Consumers = new();
            internal int RemainingDependencies;
            internal bool IsScheduled;

            internal Node(ModuleRegistration registration) => Registration = registration;
        }

        internal static List<List<ModuleRegistration>> Build(IReadOnlyList<ModuleRegistration> registrations)
        {
            var nodes = new List<Node>(registrations.Count);
            var lookup = new Dictionary<Type, Node>(registrations.Count);
            foreach (ModuleRegistration registration in registrations)
            {
                var node = new Node(registration);
                lookup.Add(registration.Type, node);
                nodes.Add(node);
            }

            foreach (Node node in nodes)
            {
                foreach (Type dependency in node.Registration.Dependencies)
                {
                    if (lookup.TryGetValue(dependency, out Node provider))
                    {
                        node.RemainingDependencies++;
                        provider.Consumers.Add(node);
                    }
                    else
                    {
                        throw new InvalidOperationException($"模块 {node.Registration.Type.FullName} 缺少依赖 {dependency.FullName}。");
                    }
                }
            }

            var stages = new List<List<ModuleRegistration>>();
            var ready = new List<Node>();
            int remaining = nodes.Count;
            while (remaining > 0)
            {
                // 先满足依赖，同一批次内按声明顺序启动。
                ready.Clear();
                foreach (Node node in nodes)
                {
                    if (node.IsScheduled || node.RemainingDependencies != 0) continue;
                    ready.Add(node);
                }
                if (ready.Count == 0) throw new InvalidOperationException($"模块依赖存在循环：{DescribeCycle(nodes, lookup)}。");

                var stage = new List<ModuleRegistration>(ready.Count);
                foreach (Node node in ready)
                {
                    node.IsScheduled = true;
                    stage.Add(node.Registration);
                    foreach (Node consumer in node.Consumers) consumer.RemainingDependencies--;
                }
                stages.Add(stage);
                remaining -= ready.Count;
            }
            return stages;
        }

        private static string DescribeCycle(List<Node> nodes, Dictionary<Type, Node> lookup)
        {
            Node current = nodes.Find(node => !node.IsScheduled);
            var positions = new Dictionary<Node, int>();
            var names = new List<string>();
            while (!positions.TryGetValue(current, out _))
            {
                positions.Add(current, names.Count);
                names.Add(current.Registration.Type.FullName);
                foreach (Type dependency in current.Registration.Dependencies)
                {
                    if (!lookup.TryGetValue(dependency, out Node provider) || provider.IsScheduled) continue;
                    current = provider;
                    break;
                }
            }
            names.RemoveRange(0, positions[current]);
            names.Add(current.Registration.Type.FullName);
            return string.Join(" -> ", names);
        }
    }
}
