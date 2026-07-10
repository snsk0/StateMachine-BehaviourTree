using System.Collections.Generic;
 
 namespace HFSM
 {
     public abstract class NodeBase<T>
     {
         protected internal T Context => _context;
         internal NodeBase<T> Parent => _parent;
         internal IReadOnlyList<NodeBase<T>> Children => _children;

         private readonly T _context;
         private readonly NodeBase<T> _parent;
         protected readonly List<NodeBase<T>> _children;
         
         //状態(BT用)
         internal NodeStatus Status { get; set; }
         
         protected NodeBase(T context, NodeBase<T> parent = null)
         {
             _context = context;
             _parent = parent;
             _children = new List<NodeBase<T>>();
             Status = NodeStatus.InActive;
         }
         
         protected internal abstract void OnEnter();
         protected internal abstract void OnExit();
         protected internal virtual NodeStatus Tick()
         {
             return Status;
         }
     }
 }