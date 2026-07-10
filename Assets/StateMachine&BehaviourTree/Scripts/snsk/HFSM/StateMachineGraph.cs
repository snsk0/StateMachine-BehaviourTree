using System;
using System.Collections.Generic;
using UnityEngine;

namespace HFSM
{
    [CreateAssetMenu(fileName = "StateMachineGraph", menuName = "HFSM/State Machine Graph")]
    public sealed class StateMachineGraph : ScriptableObject
    {
        [SerializeField] private List<StateNodeData> _nodes = new List<StateNodeData>();
        [SerializeField] private List<StateTransitionData> _transitions = new List<StateTransitionData>();
        [SerializeField] private string _entryNodeId;
        [SerializeField] private string _contextTypeName;

        public IReadOnlyList<StateNodeData> Nodes => _nodes;
        public IReadOnlyList<StateTransitionData> Transitions => _transitions;

        public string EntryNodeId
        {
            get => _entryNodeId;
            set => _entryNodeId = value;
        }

        public string ContextTypeName
        {
            get => _contextTypeName;
            set => _contextTypeName = value;
        }

        public List<StateNodeData> MutableNodes => _nodes;
        public List<StateTransitionData> MutableTransitions => _transitions;

        public StateNodeData FindNode(string nodeId)
        {
            return _nodes.Find(node => node.Id == nodeId);
        }
    }

    [Serializable]
    public sealed class StateNodeData
    {
        [SerializeField] private string _id;
        [SerializeField] private string _name = "State";
        [SerializeField] private string _parentNodeId;
        [SerializeField] private string _initialChildNodeId;
        [SerializeField] private string _stateTypeName;
        [SerializeField] private Vector2 _position;

        public string Id
        {
            get => _id;
            set => _id = value;
        }

        public string Name
        {
            get => _name;
            set => _name = value;
        }

        public string ParentNodeId
        {
            get => _parentNodeId;
            set => _parentNodeId = value;
        }

        public string InitialChildNodeId
        {
            get => _initialChildNodeId;
            set => _initialChildNodeId = value;
        }

        public string StateTypeName
        {
            get => _stateTypeName;
            set => _stateTypeName = value;
        }

        public Vector2 Position
        {
            get => _position;
            set => _position = value;
        }
    }

    [Serializable]
    public sealed class StateTransitionData
    {
        [SerializeField] private string _fromNodeId;
        [SerializeField] private string _toNodeId;
        [SerializeField] private int _eventId;
        [SerializeField] private int _priority;

        public string FromNodeId
        {
            get => _fromNodeId;
            set => _fromNodeId = value;
        }

        public string ToNodeId
        {
            get => _toNodeId;
            set => _toNodeId = value;
        }

        public int EventId
        {
            get => _eventId;
            set => _eventId = value;
        }

        public int Priority
        {
            get => _priority;
            set => _priority = value;
        }
    }
}
