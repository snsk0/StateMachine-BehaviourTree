using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HFSM
{
    public static class StateMachineGraphBuilder
    {
        public static StateMachine<T> Build<T>(
            StateMachineGraph graph,
            T context,
            Func<StateTransitionData, Func<bool>> guardResolver = null)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (!string.IsNullOrWhiteSpace(graph.ContextTypeName))
            {
                var graphContextType = Type.GetType(graph.ContextTypeName, false);
                if (graphContextType == null)
                {
                    throw new InvalidOperationException("Graph context type could not be resolved: " + graph.ContextTypeName);
                }

                if (!graphContextType.IsSubclassOf(typeof(StateMachineContext)))
                {
                    throw new InvalidOperationException(
                        "Graph context type must inherit HFSM.StateMachineContext: " + graphContextType.FullName);
                }

                if (graphContextType != typeof(T))
                {
                    throw new InvalidOperationException(
                        "Graph context type mismatch. Graph=" + graphContextType.FullName + " Build<" + typeof(T).FullName + ">");
                }
            }

            var rootMachine = new StateMachine<T>(context);
            var nodeMap = graph.Nodes.ToDictionary(n => n.Id, n => n);
            var stateMap = new Dictionary<string, StateBase<T>>();
            var machineMap = new Dictionary<string, StateMachine<T>>();

            BuildChildren(graph, context, nodeMap, stateMap, machineMap, rootMachine, null);
            ConfigureInitialStates(graph, nodeMap, stateMap, machineMap, rootMachine);
            BuildTransitions(graph, stateMap, guardResolver);

            return rootMachine;
        }

        private static void BuildChildren<T>(
            StateMachineGraph graph,
            T context,
            Dictionary<string, StateNodeData> nodeMap,
            Dictionary<string, StateBase<T>> stateMap,
            Dictionary<string, StateMachine<T>> machineMap,
            StateMachine<T> parentMachine,
            string parentNodeId)
        {
            var children = graph.Nodes
                .Where(n => NormalizeNodeId(n.ParentNodeId) == NormalizeNodeId(parentNodeId))
                .ToList();

            foreach (var childNode in children)
            {
                var state = CreateStateInstance<T>(childNode, context, parentMachine);
                parentMachine.AddChild(state);
                stateMap[childNode.Id] = state;
            }

            foreach (var childNode in children)
            {
                var hasNested = graph.Nodes.Any(n => NormalizeNodeId(n.ParentNodeId) == childNode.Id);
                if (!hasNested)
                {
                    continue;
                }

                var childState = stateMap[childNode.Id];
                var subMachine = childState.CreateSubMachine();
                machineMap[childNode.Id] = subMachine;
                BuildChildren(graph, context, nodeMap, stateMap, machineMap, subMachine, childNode.Id);
            }
        }

        private static void ConfigureInitialStates<T>(
            StateMachineGraph graph,
            Dictionary<string, StateNodeData> nodeMap,
            Dictionary<string, StateBase<T>> stateMap,
            Dictionary<string, StateMachine<T>> machineMap,
            StateMachine<T> rootMachine)
        {
            if (!string.IsNullOrWhiteSpace(graph.EntryNodeId)
                && stateMap.TryGetValue(graph.EntryNodeId, out var entryState)
                && nodeMap.TryGetValue(graph.EntryNodeId, out var entryNode))
            {
                var rootEntry = FindTopLevelAncestor(nodeMap, entryNode);
                if (rootEntry != null && stateMap.TryGetValue(rootEntry.Id, out var rootEntryState))
                {
                    rootMachine.InitialState = rootEntryState;
                }
                else
                {
                    rootMachine.InitialState = entryState;
                }
            }

            SetInitialState(rootMachine);

            foreach (var pair in machineMap)
            {
                var ownerNodeId = pair.Key;
                var subMachine = pair.Value;
                if (!nodeMap.TryGetValue(ownerNodeId, out var ownerNode))
                {
                    SetInitialState(subMachine);
                    continue;
                }

                if (!TrySetInitialFromNodeId(subMachine, ownerNode.InitialChildNodeId, stateMap))
                {
                    SetInitialState(subMachine);
                }
            }
        }

        private static void BuildTransitions<T>(
            StateMachineGraph graph,
            Dictionary<string, StateBase<T>> stateMap,
            Func<StateTransitionData, Func<bool>> guardResolver)
        {
            foreach (var transition in graph.Transitions)
            {
                if (!stateMap.TryGetValue(transition.FromNodeId, out var fromState))
                {
                    continue;
                }

                if (!stateMap.TryGetValue(transition.ToNodeId, out var targetState))
                {
                    continue;
                }

                var guard = guardResolver != null ? guardResolver(transition) : null;
                fromState.AddTransition(new Transition<T>(
                    transition.Priority,
                    transition.EventId,
                    targetState,
                    guard ?? (() => true)));
            }
        }

        private static StateBase<T> CreateStateInstance<T>(StateNodeData node, T context, StateMachine<T> parentMachine)
        {
            if (string.IsNullOrWhiteSpace(node.StateTypeName))
            {
                return new PlaceholderState<T>(context, parentMachine, node.Name);
            }

            var stateType = Type.GetType(node.StateTypeName, true);
            stateType = CloseStateTypeForContext(stateType, typeof(T));

            if (!IsStateType(stateType))
            {
                throw new InvalidOperationException("Type does not inherit StateBase<>: " + stateType.FullName);
            }

            if (!typeof(StateBase<T>).IsAssignableFrom(stateType))
            {
                throw new InvalidOperationException(
                    "Type is not compatible with context " + typeof(T).FullName + ": " + stateType.FullName);
            }

            if (stateType.IsAbstract)
            {
                throw new InvalidOperationException("State type is abstract: " + stateType.FullName);
            }

            var ctor = FindConstructor<T>(stateType);
            if (ctor == null)
            {
                throw new MissingMethodException(stateType.FullName, ".ctor(context, StateMachine<T>)");
            }

            object[] args = { context, parentMachine };
            var instance = ctor.Invoke(args);
            return (StateBase<T>)instance;
        }

        private static Type CloseStateTypeForContext(Type stateType, Type contextType)
        {
            if (!stateType.IsGenericTypeDefinition)
            {
                return stateType;
            }

            var genericArgs = stateType.GetGenericArguments();
            if (genericArgs.Length == 1)
            {
                return stateType.MakeGenericType(contextType);
            }

            throw new InvalidOperationException(
                "Generic state type with multiple type parameters is not supported: " + stateType.FullName);
        }

        private sealed class PlaceholderState<TContext> : StateBase<TContext>
        {
            private readonly string _nodeName;

            public PlaceholderState(TContext context, StateMachine<TContext> parent, string nodeName) : base(context, parent)
            {
                _nodeName = string.IsNullOrWhiteSpace(nodeName) ? "(Unnamed)" : nodeName;
            }

            protected internal override void OnEnter()
            {
            }

            protected internal override void OnExit()
            {
            }

            protected override void OnUpdate()
            {
            }

            public override string ToString()
            {
                return "PlaceholderState:" + _nodeName;
            }
        }

        private static ConstructorInfo FindConstructor<T>(Type stateType)
        {
            var machineType = typeof(StateMachine<T>);
            var contextType = typeof(T);
            var ctors = stateType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var ctor in ctors)
            {
                var parameters = ctor.GetParameters();
                if (parameters.Length != 2)
                {
                    continue;
                }

                var contextParam = parameters[0].ParameterType;
                var machineParam = parameters[1].ParameterType;
                if (!contextParam.IsAssignableFrom(contextType))
                {
                    continue;
                }

                if (!machineParam.IsAssignableFrom(machineType))
                {
                    continue;
                }

                return ctor;
            }

            return null;
        }

        private static bool IsStateType(Type type)
        {
            var current = type;
            while (current != null)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(StateBase<>))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private static void SetInitialState<T>(StateMachine<T> machine)
        {
            if (machine.Children.Count == 0)
            {
                return;
            }

            if (machine.InitialState == null && machine.Children[0] is StateBase<T> state)
            {
                machine.InitialState = state;
            }
        }

        private static bool TrySetInitialFromNodeId<T>(
            StateMachine<T> machine,
            string nodeId,
            Dictionary<string, StateBase<T>> stateMap)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            if (!stateMap.TryGetValue(nodeId, out var state))
            {
                return false;
            }

            if (!machine.Children.Contains(state))
            {
                return false;
            }

            machine.InitialState = state;
            return true;
        }

        private static StateNodeData FindTopLevelAncestor(Dictionary<string, StateNodeData> nodeMap, StateNodeData node)
        {
            var current = node;
            var visited = new HashSet<string>();
            while (current != null && !string.IsNullOrWhiteSpace(current.ParentNodeId))
            {
                if (!visited.Add(current.Id))
                {
                    break;
                }

                if (!nodeMap.TryGetValue(current.ParentNodeId, out var parent))
                {
                    break;
                }

                current = parent;
            }

            return current;
        }

        private static string NormalizeNodeId(string nodeId)
        {
            return string.IsNullOrWhiteSpace(nodeId) ? null : nodeId;
        }
    }
}
