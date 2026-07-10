using UnityEngine;

namespace HFSM
{
    public class UnitTest : MonoBehaviour
    {
        [SerializeField] private StateMachineGraph graph;
        
        void Awake()
        { 
            var fsm = new StateMachine<GameObject>(gameObject);
            var start = new StateTest<GameObject>(gameObject, fsm);
            var end = new StateTest<GameObject>(gameObject, fsm);
            var subMachine = end.CreateSubMachine();
            var end1 = new StateTest<GameObject>(gameObject, subMachine);
            var end2 = new StateTest<GameObject>(gameObject, subMachine);

            var transition = new Transition<GameObject>(
                0, 0, end2, () => true
            );

            fsm.AddChild(start);
            fsm.AddChild(end);
            fsm.InitialState = start;
            subMachine.InitialState = end1;
            subMachine.AddChild(end1);
            subMachine.AddChild(end2);
            
            start.AddTransition(transition);

            fsm.Start();
            fsm.SendEvent(0);
            
            var fsm2 = StateMachineGraphBuilder.Build<GameObject>(graph, gameObject);
            fsm2.Start();
            fsm2.SendEvent(0);
        }
    }

    public class StateTest<GameObject> : StateBase<GameObject>
    {
        public StateTest(GameObject context, StateMachine<GameObject> parent) : base(context, parent)
        {
        }

        protected internal override void OnEnter()
        {
            Debug.Log("Enter");
        }

        protected internal override void OnExit()
        {
            Debug.Log("Exit");
        }

        protected override void OnUpdate()
        {
            
        }
    }
    
    public class StateTest2<int32> : StateBase<int32>
    {
        public StateTest2(int32 context, StateMachine<int32> parent) : base(context, parent)
        {
        }

        protected internal override void OnEnter()
        {
            Debug.Log("Enter2");
        }

        protected internal override void OnExit()
        {
            Debug.Log("Exit2");
        }

        protected override void OnUpdate()
        {
            
        }
    }
}