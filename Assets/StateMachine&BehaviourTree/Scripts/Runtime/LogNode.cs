using UnityEngine;

[CreateAssetMenu(menuName = "ArborLike/LogNode")]
public class LogNode : BaseNode
{
    public string message = "Hello Node";

    public override void Execute()
    {
        Debug.Log(message);
    }
}
