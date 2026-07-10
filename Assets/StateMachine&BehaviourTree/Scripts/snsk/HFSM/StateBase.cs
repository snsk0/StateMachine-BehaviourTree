using System;
using System.Collections.Generic;

namespace HFSM
{
    public abstract class StateBase<T> : NodeBase<T>
    {
        public IReadOnlyList<Transition<T>> Transitions => _transitions;
        private readonly List<Transition<T>> _transitions;

        protected StateBase(T context, StateMachine<T> parent) : base(context, parent)
        {
            _transitions = new List<Transition<T>>();
        }

        public void AddTransition(Transition<T> transition)
        {
            _transitions.Add(transition);
        }

        public StateMachine<T> CreateSubMachine()
        {
            if (_children.Count == 0)
            {
                _children.Add(new StateMachine<T>(Context, this));
                return (StateMachine<T>)_children[0];
            }
            throw new Exception("すでにサブノードが存在しています。");
        }
        
        protected internal override NodeStatus Tick()
        {
            OnUpdate();
            return Status;
        }

        protected abstract void OnUpdate();

        protected internal bool SendEvent(int eventId)
        {
            if (_children.Count == 1 && _children[0] is StateMachine<T>)
            {
                var child = (StateMachine<T>)_children[0];
                return child.SendEvent(eventId);
            }

            return false;
        }
    }
}