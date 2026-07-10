using System;

namespace HFSM
{
    public class Transition<T>
    {
        private readonly int _priority;
        private readonly int _eventId;
        private readonly StateBase<T> _target;
        private readonly Func<bool> _canTransition;
        
        public int Priority => _priority;
        public int EventId => _eventId;
        public StateBase<T> Target => _target;
        public Func<bool> CanTransition => _canTransition;

        public Transition(int priority, int eventId, StateBase<T> target, Func<bool> canTransition)
        {
            _priority = priority;
            _eventId = eventId;
            _target = target;
            _canTransition = canTransition;
        }
    }
}