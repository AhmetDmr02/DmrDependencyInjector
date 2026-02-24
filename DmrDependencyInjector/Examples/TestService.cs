using DmrDependencyInjector;
using UnityEngine;

public class TestService : MonoBehaviour
{
    private void Awake()
    {
        this.Register();
    }

    private void OnDestroy()
    {
        this.Unregister();
    }

    public void ExecuteTask()
    {
        Debug.Log("Task Executed!");
    }
}