using DmrDependencyInjector;
using UnityEngine;

public class ClientBehaviour : MonoBehaviour
{
    [DmrInject] private TestService _testService;

    private void Awake()
    {
        this.Inject();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            _testService?.ExecuteTask();
        }
    }
}