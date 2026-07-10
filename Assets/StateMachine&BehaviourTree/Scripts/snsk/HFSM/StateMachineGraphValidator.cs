using System;
using System.Collections.Generic;
using System.Reflection;

namespace HFSM
{
    public static class StateMachineGraphValidator
    {
        public static List<string> Validate(StateMachineGraph graph)
        {
            var errors = new List<string>();
            if (graph == null)
            {
                errors.Add("Graph is null.");
                return errors;
            }

            Type graphContextType = null;
            if (string.IsNullOrWhiteSpace(graph.ContextTypeName))
            {
                errors.Add("Graph context type is not set.");
            }
            else
            {
                graphContextType = Type.GetType(graph.ContextTypeName, false);
                if (graphContextType == null)
                {
                    errors.Add("Graph context type could not be resolved: " + graph.ContextTypeName);
                }
                else if (!graphContextType.IsSubclassOf(typeof(StateMachineContext)))
                {
                    errors.Add("Graph context type must inherit HFSM.StateMachineContext: " + graphContextType.FullName);
                }
            }

            var nodeIds = new HashSet<string>();
            foreach (var node in graph.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    errors.Add("A node has an empty id.");
                    continue;
                }

                if (!nodeIds.Add(node.Id))
                {
                    errors.Add("Duplicate node id: " + node.Id);
                }
            }

            foreach (var node in graph.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.ParentNodeId))
                {
                    continue;
                }

                if (node.ParentNodeId == node.Id)
                {
                    errors.Add("Node cannot parent itself: " + node.Id);
                    continue;
                }

                if (!nodeIds.Contains(node.ParentNodeId))
                {
                    errors.Add("Parent node does not exist for " + node.Id + ": " + node.ParentNodeId);
                }
            }

            foreach (var node in graph.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.InitialChildNodeId))
                {
                    continue;
                }

                if (!nodeIds.Contains(node.InitialChildNodeId))
                {
                    errors.Add("Initial child does not exist for node " + node.Id + ": " + node.InitialChildNodeId);
                    continue;
                }

                var initialChild = graph.FindNode(node.InitialChildNodeId);
                if (initialChild == null || initialChild.ParentNodeId != node.Id)
                {
                    errors.Add("Initial child must be a direct child of node " + node.Id + ": " + node.InitialChildNodeId);
                }
            }

            foreach (var node in graph.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.StateTypeName))
                {
                    continue;
                }

                var stateType = Type.GetType(node.StateTypeName, false);
                if (stateType == null)
                {
                    errors.Add("State type could not be resolved for node " + node.Id + ": " + node.StateTypeName);
                    continue;
                }

                stateType = CloseStateTypeForContext(stateType, graphContextType, out var closeError);
                if (closeError != null)
                {
                    errors.Add("State type is not compatible with graph context for node " + node.Id + ": " + closeError);
                    continue;
                }

                if (!IsStateType(stateType))
                {
                    errors.Add("State type must inherit HFSM.StateBase<> for node " + node.Id + ": " + stateType.FullName);
                    continue;
                }

                if (graphContextType != null && !IsStateTypeCompatibleWithContext(stateType, graphContextType))
                {
                    errors.Add("State type context mismatch for node " + node.Id + ": " + stateType.FullName);
                    continue;
                }

                if (stateType.IsAbstract)
                {
                    errors.Add("State type is abstract for node " + node.Id + ": " + stateType.FullName);
                }

                if (!HasCompatibleConstructor(stateType))
                {
                    errors.Add("State type requires ctor(context, StateMachine<T>) for node " + node.Id + ": " + stateType.FullName);
                }
            }

            foreach (var node in graph.Nodes)
            {
                var visited = new HashSet<string> { node.Id };
                string parentId = node.ParentNodeId;
                while (!string.IsNullOrWhiteSpace(parentId))
                {
                    if (!visited.Add(parentId))
                    {
                        errors.Add("Parent cycle detected at node: " + node.Id);
                        break;
                    }

                    var parentNode = graph.FindNode(parentId);
                    if (parentNode == null)
                    {
                        break;
                    }

                    parentId = parentNode.ParentNodeId;
                }
            }

            if (!string.IsNullOrWhiteSpace(graph.EntryNodeId) && !nodeIds.Contains(graph.EntryNodeId))
            {
                errors.Add("Entry node id does not exist: " + graph.EntryNodeId);
            }

            foreach (var transition in graph.Transitions)
            {
                if (!nodeIds.Contains(transition.FromNodeId))
                {
                    errors.Add("Transition source does not exist: " + transition.FromNodeId);
                }

                if (!nodeIds.Contains(transition.ToNodeId))
                {
                    errors.Add("Transition target does not exist: " + transition.ToNodeId);
                }
            }

            return errors;
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

        private static bool HasCompatibleConstructor(Type stateType)
        {
            var ctors = stateType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var ctor in ctors)
            {
                var parameters = ctor.GetParameters();
                if (parameters.Length != 2)
                {
                    continue;
                }

                var machineParam = parameters[1].ParameterType;
                if (machineParam.IsGenericType && machineParam.GetGenericTypeDefinition() == typeof(StateMachine<>))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsStateTypeCompatibleWithContext(Type stateType, Type contextType)
        {
            if (contextType == null)
            {
                return true;
            }

            var targetType = typeof(StateBase<>).MakeGenericType(contextType);
            return targetType.IsAssignableFrom(stateType);
        }

        private static Type CloseStateTypeForContext(Type stateType, Type contextType, out string error)
        {
            error = null;
            if (!stateType.IsGenericTypeDefinition)
            {
                return stateType;
            }

            if (contextType == null)
            {
                error = "graph context is not resolved.";
                return null;
            }

            var genericArgs = stateType.GetGenericArguments();
            if (genericArgs.Length != 1)
            {
                error = "generic state type with multiple type parameters is not supported: " + stateType.FullName;
                return null;
            }

            try
            {
                return stateType.MakeGenericType(contextType);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }
    }
}
