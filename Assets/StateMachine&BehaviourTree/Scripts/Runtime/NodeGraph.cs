using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ArborLike/NodeGraph")]
public class NodeGraph : ScriptableObject
{
    public List<BaseNode> nodes = new List<BaseNode>();
    public List<NodeConnection> connections = new List<NodeConnection>();
}
