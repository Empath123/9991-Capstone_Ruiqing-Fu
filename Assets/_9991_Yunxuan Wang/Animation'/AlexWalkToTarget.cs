using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class AlexWalkToTarget : MonoBehaviour
{
    public Transform target;
    public Animator characterAnimator;

    private NavMeshAgent agent;
    private bool walking;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        if (characterAnimator == null)
            characterAnimator = GetComponentInChildren<Animator>();

        if (characterAnimator != null)
        {
            characterAnimator.applyRootMotion = false;
            characterAnimator.speed = 0f;
        }

        if (target == null)
        {
            Debug.LogError("请给 Target 指定 WalkTarget。", this);
            return;
        }

        if (!agent.isOnNavMesh)
        {
            Debug.LogError("Alex 没有站在导航网格上。", this);
            return;
        }

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        // 在终点附近寻找导航网格上的位置。
        if (!NavMesh.SamplePosition(
            target.position, out NavMeshHit hit, 0.5f, filter))
        {
            Debug.LogError("WalkTarget 附近没有可行走区域。", this);
            return;
        }

        var path = new NavMeshPath();

        if (!agent.CalculatePath(hit.position, path) ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            Debug.LogError(
                "到终点的路线不连通，请检查蓝色导航区域。", this);
            return;
        }

        agent.isStopped = false;
        walking = agent.SetPath(path);

        if (!walking)
            Debug.LogError("无法设置移动路线。", this);
    }

    void Update()
    {
        if (!walking || !agent.isOnNavMesh)
            return;

        // 实际移动时播放动画，停下时暂停动画。
        if (characterAnimator != null)
        {
            characterAnimator.speed =
                agent.velocity.sqrMagnitude > 0.01f ? 1f : 0f;
        }

        // 到达终点并减速停止。
        if (!agent.pathPending &&
            agent.remainingDistance <= agent.stoppingDistance + 0.05f &&
            agent.velocity.sqrMagnitude < 0.01f)
        {
            agent.isStopped = true;
            walking = false;

            if (characterAnimator != null)
                characterAnimator.speed = 0f;
        }
    }
}