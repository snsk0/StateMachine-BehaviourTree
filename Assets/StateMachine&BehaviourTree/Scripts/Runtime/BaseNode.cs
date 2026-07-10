using UnityEngine;

public abstract class BaseNode : ScriptableObject
{
    public Vector2 position;
    public Vector2 size = new Vector2(200, 100);

    public virtual void Execute() { }
}
