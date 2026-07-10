using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HFSM.Editor
{
    internal sealed class StateMachineGraphController
    {
        private readonly StateMachineGraph _graph;

        public StateMachineGraphController(StateMachineGraph graph)
        {
            _graph = graph;
        }

        public StateNodeData AddNode(Vector2 position, string parentNodeId = null)
        {
            Undo.RecordObject(_graph, "Add State Node");
            var node = new StateNodeData
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "State",
                ParentNodeId = NormalizeNodeId(parentNodeId),
                Position = position
            };
            _graph.MutableNodes.Add(node);

            if (string.IsNullOrWhiteSpace(_graph.EntryNodeId))
            {
                _graph.EntryNodeId = node.Id;
            }

            MarkDirty();
            return node;
        }

        public void MoveNode(string nodeId, Vector2 position)
        {
            var node = _graph.FindNode(nodeId);
            if (node == null)
            {
                return;
            }

            Undo.RecordObject(_graph, "Move State Node");
            node.Position = position;
            MarkDirty();
        }

        public void RenameNode(string nodeId, string newName)
        {
            var node = _graph.FindNode(nodeId);
            if (node == null)
            {
                return;
            }

            Undo.RecordObject(_graph, "Rename State Node");
            node.Name = string.IsNullOrWhiteSpace(newName) ? "State" : newName;
            MarkDirty();
        }

        public void SetNodeStateType(string nodeId, string stateTypeName)
        {
            var node = _graph.FindNode(nodeId);
            if (node == null)
            {
                return;
            }

            Undo.RecordObject(_graph, "Set State Node Type");
            node.StateTypeName = string.IsNullOrWhiteSpace(stateTypeName) ? null : stateTypeName;
            MarkDirty();
        }

        public void SetGraphContextType(string contextTypeName)
        {
            Undo.RecordObject(_graph, "Set Graph Context Type");
            _graph.ContextTypeName = string.IsNullOrWhiteSpace(contextTypeName) ? null : contextTypeName;
            MarkDirty();
        }

        public void RemoveNode(string nodeId)
        {
            Undo.RecordObject(_graph, "Remove State Node");
            var removeIds = CollectDescendantIds(nodeId);
            removeIds.Add(nodeId);

            _graph.MutableNodes.RemoveAll(node => removeIds.Contains(node.Id));
            _graph.MutableTransitions.RemoveAll(t => removeIds.Contains(t.FromNodeId) || removeIds.Contains(t.ToNodeId));
            foreach (var node in _graph.MutableNodes)
            {
                if (!string.IsNullOrWhiteSpace(node.InitialChildNodeId) && removeIds.Contains(node.InitialChildNodeId))
                {
                    node.InitialChildNodeId = null;
                }
            }

            if (removeIds.Contains(_graph.EntryNodeId))
            {
                _graph.EntryNodeId = _graph.Nodes.FirstOrDefault()?.Id;
            }

            MarkDirty();
        }

        public void SetInitialChildNode(string parentNodeId, string childNodeId)
        {
            var parent = _graph.FindNode(parentNodeId);
            if (parent == null)
            {
                return;
            }

            string normalizedChild = NormalizeNodeId(childNodeId);
            if (!string.IsNullOrEmpty(normalizedChild))
            {
                var child = _graph.FindNode(normalizedChild);
                if (child == null || NormalizeNodeId(child.ParentNodeId) != parent.Id)
                {
                    return;
                }
            }

            Undo.RecordObject(_graph, "Set Hierarchy Entry State");
            parent.InitialChildNodeId = normalizedChild;
            MarkDirty();
        }

        public void SetEntryNode(string nodeId)
        {
            if (_graph.FindNode(nodeId) == null)
            {
                return;
            }

            Undo.RecordObject(_graph, "Set Entry State");
            _graph.EntryNodeId = nodeId;
            MarkDirty();
        }

        public void AddTransition(string fromNodeId, string toNodeId, int eventId, int priority)
        {
            if (_graph.FindNode(fromNodeId) == null || _graph.FindNode(toNodeId) == null)
            {
                return;
            }

            Undo.RecordObject(_graph, "Add Transition");
            _graph.MutableTransitions.Add(new StateTransitionData
            {
                FromNodeId = fromNodeId,
                ToNodeId = toNodeId,
                EventId = eventId,
                Priority = priority
            });
            MarkDirty();
        }

        public void RemoveTransitionAt(int index)
        {
            if (index < 0 || index >= _graph.MutableTransitions.Count)
            {
                return;
            }

            Undo.RecordObject(_graph, "Remove Transition");
            _graph.MutableTransitions.RemoveAt(index);
            MarkDirty();
        }

        public void ReparentNode(string nodeId, string newParentNodeId)
        {
            var node = _graph.FindNode(nodeId);
            if (node == null)
            {
                return;
            }

            string normalizedParent = NormalizeNodeId(newParentNodeId);
            if (node.Id == normalizedParent)
            {
                return;
            }

            var descendants = CollectDescendantIds(nodeId);
            if (!string.IsNullOrEmpty(normalizedParent) && descendants.Contains(normalizedParent))
            {
                return;
            }

            Undo.RecordObject(_graph, "Reparent State Node");
            foreach (var n in _graph.MutableNodes)
            {
                if (n.InitialChildNodeId == nodeId)
                {
                    n.InitialChildNodeId = null;
                }
            }
            node.ParentNodeId = normalizedParent;
            MarkDirty();
        }

        private HashSet<string> CollectDescendantIds(string nodeId)
        {
            var result = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(nodeId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var child in _graph.Nodes.Where(n => NormalizeNodeId(n.ParentNodeId) == current))
                {
                    if (!result.Add(child.Id))
                    {
                        continue;
                    }

                    queue.Enqueue(child.Id);
                }
            }

            return result;
        }

        private static string NormalizeNodeId(string nodeId)
        {
            return string.IsNullOrWhiteSpace(nodeId) ? null : nodeId;
        }

        private void MarkDirty()
        {
            EditorUtility.SetDirty(_graph);
        }
    }
}
