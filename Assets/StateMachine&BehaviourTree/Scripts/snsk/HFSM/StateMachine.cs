using System.Collections.Generic;

namespace HFSM
{
    public class StateMachine<T> : NodeBase<T>
    {
        private StateBase<T> _currentState;
        private StateBase<T> _lastActiveState;
        
        public StateBase<T> InitialState { get; set; }
        
        public StateMachine(T context, NodeBase<T> parent = null) : base(context, parent)
        {
        }

        public void Start()
        {
            OnEnter();
        }
        
        protected internal override void OnEnter()
        {
            _currentState = InitialState;
            _currentState.OnEnter();

            if (_currentState.Children.Count > 0)
            {
                _currentState.Children[0].OnEnter();
            }
        }

        protected internal override void OnExit()
        {
            if (_currentState.Children.Count > 0)
            {
                _currentState.Children[0].OnExit();
            }
            _currentState.OnExit();
            _currentState = null;
        }

        public bool AddChild(StateBase<T> child)
        {
            if (_children.Contains(child))
            {
                return false;
            }
            _children.Add(child);
            return true;
        }
        
        public bool SendEvent(int eventId)
        {
            if (_currentState != null)
            {
                bool eventProcessed = _currentState.SendEvent(eventId);
                if (eventProcessed)
                {
                    return true;
                }
            }
            
            var table = _currentState.Transitions;
            var transitions = new List<Transition<T>>();
            
            //登録済みのイベントを検索する
            foreach (var transition in table)
            {
                if (transition.EventId == eventId)
                {
                    transitions.Add(transition);
                }
            }

            //見つかったイベントからpriorityに従って一つに絞る
            int targetPriority = int.MinValue;
            StateBase<T> targetState　= null;
            foreach (var transition in transitions)
            {
                if (targetPriority < transition.Priority)
                {
                    //ガードを確認
                    if (transition.CanTransition.Invoke())
                    {
                        targetPriority = transition.Priority;
                        targetState = transition.Target;   
                    }
                }
            }

            //もし何も見つからなかったらリターン
            if (targetState == null)
            {
                return false;
            }

            //遷移先が子に含まれていなかったらLCAで共通祖先を検索
            //共通祖先までENDを繰り返す
            if (!_children.Contains(targetState))
            {
                var lca = FindLca(_currentState, targetState);
                
                lca._currentState.OnExit();
                lca._currentState = null;
                
                //StateMachineにcurrentStateを逆順に設定していく
                NodeBase<T> temp = targetState;
                while (lca != temp.Parent)
                {
                    //BTを無視した仮コード
                    ((StateMachine<T>)temp.Parent)._currentState = (StateBase<T>)temp;
                    temp = temp.Parent.Parent;
                }
                lca._currentState = (StateBase<T>)temp; //lcaも処理
                
                //上からOnEnter呼び出し
                temp = lca._currentState;
                while (temp != targetState)
                {
                    temp.OnEnter();
                    temp = ((StateMachine<T>)temp.Children[0])._currentState;
                }
                
                targetState.OnEnter(); //targetStateも処理
                if (targetState.Children.Count > 0)
                {
                    targetState.Children[0].OnEnter();
                }
                return true;
            }
            _currentState.OnExit();
            _currentState = targetState;
            _currentState.OnEnter();
            return true;
            
        }

        private StateMachine<T> FindLca(StateBase<T> from, StateBase<T> to)
        {
            //from側の最上位の親まで取得
            var fromParents = new List<NodeBase<T>>();
            NodeBase<T> temp = from;
            while (temp.Parent != null)
            {
                fromParents.Add(temp.Parent);
                temp = temp.Parent;
            }
            
            //to側の親をさかのぼってLCAをみつける
            temp = to;
            while (temp.Parent != null)
            {
                if (fromParents.Contains(temp.Parent))
                {
                    return (StateMachine<T>)temp.Parent;
                }
                temp = temp.Parent;
            }
            
            return null;
        }
    }
}